// Refresh Rate Switcher
// Copyright (C) 2026 Noel Chan
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RefreshRateSwitcher
{
    /// <summary>Settings for one monitor, keyed by <see cref="DisplayInfo.Key"/>.</summary>
    internal sealed class DisplaySettings
    {
        public string Key;
        public string Name;          // last-known friendly name (for display only)
        public bool Managed;         // false = leave this display alone
        public int PluggedInHz;      // 0 = not chosen
        public int BatteryHz;        // 0 = not chosen yet (e.g. the driver hadn't reported its rates)
        public string Port = "";     // external monitors: port last seen on, e.g. "t10c1" ("" = unknown)
    }

    /// <summary>Settings stored per-user in HKCU\Software\RefreshRateSwitcher.</summary>
    internal sealed class Settings
    {
        // Overridable so --selftest can use a scratch key and never touch real settings
        public static string KeyPath = @"Software\RefreshRateSwitcher";
        const string DisplaysSub = "Displays";
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValueName = "RefreshRateSwitcher";
        public const string BackgroundArg = "--background";

        public bool Enabled = true;
        public bool Notifications = true;
        public bool FirstRun = true;
        public readonly Dictionary<string, DisplaySettings> Displays =
            new Dictionary<string, DisplaySettings>(StringComparer.OrdinalIgnoreCase);

        // 1.1.x single-display settings, migrated to the first matching display on load
        bool _hasLegacy;
        int _legacyPluggedIn, _legacyBattery;
        string _legacyDisplay = "auto";

        public DisplaySettings Get(string key) =>
            key != null && Displays.TryGetValue(key, out var d) ? d : null;

        public static Settings Load()
        {
            var s = new Settings();
            using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
            {
                if (k == null) return s;
                s.FirstRun = false;
                s.Enabled = Convert.ToInt32(k.GetValue("Enabled", 1)) != 0;
                s.Notifications = Convert.ToInt32(k.GetValue("Notifications", 1)) != 0;

                using (var dk = k.OpenSubKey(DisplaysSub))
                {
                    if (dk != null)
                    {
                        foreach (var name in dk.GetSubKeyNames())
                        {
                            using (var e = dk.OpenSubKey(name))
                            {
                                if (e == null) continue;
                                var ds = new DisplaySettings
                                {
                                    Key = (e.GetValue("Key") as string) ?? name,
                                    Name = (e.GetValue("Name") as string) ?? "",
                                    Managed = Convert.ToInt32(e.GetValue("Managed", 0)) != 0,
                                    PluggedInHz = Convert.ToInt32(e.GetValue("PluggedInHz", 0)),
                                    BatteryHz = Convert.ToInt32(e.GetValue("BatteryHz", 0)),
                                };
                                // 1.2.0/1.2.1 didn't save the port, but put it in the ID of identical monitors
                                ds.Port = (e.GetValue("Port") as string) ?? PortFromKey(ds.Key);
                                s.Displays[ds.Key] = ds;
                            }
                        }
                    }
                    else if (k.GetValue("PluggedInHz") != null || k.GetValue("BatteryHz") != null)
                    {
                        // Settings from 1.1.x – kept in the registry (so a downgrade still works) and migrated below
                        s._hasLegacy = true;
                        s._legacyPluggedIn = Convert.ToInt32(k.GetValue("PluggedInHz", 0));
                        s._legacyBattery = Convert.ToInt32(k.GetValue("BatteryHz", 0));
                        s._legacyDisplay = (k.GetValue("Display", "auto") as string) ?? "auto";
                    }
                }
            }
            return s;
        }

        public void Save()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                k.SetValue("Enabled", Enabled ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("Notifications", Notifications ? 1 : 0, RegistryValueKind.DWord);
                using (var dk = k.CreateSubKey(DisplaysSub))
                {
                    foreach (var ds in Displays.Values)
                    {
                        using (var e = dk.CreateSubKey(SubKeyName(ds.Key)))
                        {
                            e.SetValue("Key", ds.Key, RegistryValueKind.String);
                            e.SetValue("Name", ds.Name ?? "", RegistryValueKind.String);
                            e.SetValue("Managed", ds.Managed ? 1 : 0, RegistryValueKind.DWord);
                            e.SetValue("PluggedInHz", ds.PluggedInHz, RegistryValueKind.DWord);
                            e.SetValue("BatteryHz", ds.BatteryHz, RegistryValueKind.DWord);
                            e.SetValue("Port", ds.Port ?? "", RegistryValueKind.String);
                        }
                    }
                }
            }
            FirstRun = false;
        }

        // Registry key names can't contain '\' and are limited to 255 characters
        static string SubKeyName(string key)
        {
            var n = key.Replace('\\', '/');
            if (n.Length > 200) n = n.Substring(0, 160) + "~" + ((uint)StableHash(key)).ToString("X8");
            return n;
        }

        static int StableHash(string s)
        {
            unchecked { int h = 23; foreach (char c in s) h = h * 31 + char.ToUpperInvariant(c); return h; }
        }

        // "edid:AC10-A0B1:t10c1#DISPLAY3" → model "edid:AC10-A0B1", port "t10c1"
        static string[] KeyParts(string key) => (key ?? "").Split('#')[0].Split(':');

        internal static string ModelFromKey(string key)
        {
            var p = KeyParts(key);
            return p.Length >= 2 && p[0].Equals("edid", StringComparison.OrdinalIgnoreCase) ? "edid:" + p[1] : null;
        }

        internal static string PortFromKey(string key)
        {
            var p = KeyParts(key);
            return p.Length >= 3 && ModelFromKey(key) != null ? p[2] : "";
        }

        /// <summary>
        /// Points each external monitor at the settings it was saved under, so a monitor keeps its
        /// identity when an identical one is plugged in or unplugged. Monitors of the same model are
        /// matched by the port they were last seen on. Whatever is left over is paired up in a fixed
        /// order: ports in order, saved settings with an unknown port (saved by 1.2.1 or earlier for a
        /// lone monitor) first, then by ID. So a lone monitor on a new port or dock keeps its settings,
        /// and a second identical monitor gets new settings instead of taking over the first one's.
        /// Two monitors with the same model and no serial number can only be told apart by port:
        /// swap their cables and they swap settings.
        /// Updates each display's Key; returns true if a saved port changed (settings need saving).
        /// </summary>
        public bool BindKeys(List<DisplayInfo> displays)
        {
            bool changed = false;
            var cmp = StringComparer.OrdinalIgnoreCase;
            var used = new HashSet<string>(displays.Where(d => !d.IsEdidExternal).Select(d => d.Key), cmp);

            foreach (var group in displays.Where(d => d.IsEdidExternal).GroupBy(d => d.ModelKey, cmp))
            {
                var connected = group.OrderBy(d => d.OutputTech).ThenBy(d => d.Connector)
                                     .ThenBy(d => d.DeviceName, cmp).ToList();
                var saved = Displays.Values.Where(r => cmp.Equals(ModelFromKey(r.Key), group.Key) && !used.Contains(r.Key))
                                    .OrderBy(r => string.IsNullOrEmpty(r.Port) ? 0 : 1)
                                    .ThenBy(r => r.Key, cmp).ToList();
                var match = new Dictionary<DisplayInfo, DisplaySettings>();

                foreach (var d in connected)            // 1. same model, same port
                {
                    var r = saved.FirstOrDefault(x => !string.IsNullOrEmpty(x.Port) && cmp.Equals(x.Port, d.Port));
                    if (r != null) { match[d] = r; saved.Remove(r); }
                }
                foreach (var d in connected)            // 2. leftovers, in a fixed order
                {
                    if (match.ContainsKey(d) || saved.Count == 0) continue;
                    match[d] = saved[0]; saved.RemoveAt(0);
                }

                foreach (var d in connected)
                {
                    if (match.TryGetValue(d, out var r))
                    {
                        d.Key = r.Key;
                        if (!cmp.Equals(r.Port ?? "", d.Port)) { r.Port = d.Port; changed = true; }
                    }
                    else
                    {
                        // Not seen before: plain model ID if it's free, otherwise model + port
                        string k = group.Key;
                        if (Displays.ContainsKey(k) || used.Contains(k)) k = group.Key + ":" + d.Port;
                        for (int n = 2; Displays.ContainsKey(k) || used.Contains(k); n++) k = $"{group.Key}:{d.Port}#{n}";
                        d.Key = k;
                    }
                    used.Add(d.Key);
                }
            }
            return changed;
        }

        static int SuggestedBattery(List<int> rates) => rates.Contains(60) ? 60 : rates.Min();

        /// <summary>
        /// Creates settings for displays seen for the first time. Returns true if anything was added.
        ///  - 1.1.x settings are carried over to the display that version controlled.
        ///  - New built-in panels (or the main display of a PC without one) are switched automatically:
        ///    highest rate when plugged in, 60 Hz (or the lowest rate) on battery.
        ///  - New external monitors get the same suggested rates but are left alone until ticked,
        ///    so plugging in a monitor never changes its rate unexpectedly.
        ///  - A rate still 0 (the driver reported no rates yet, e.g. right after docking or waking)
        ///    is filled in once rates are reported. A rate that was chosen is never replaced.
        /// </summary>
        public bool EnsureDisplays(List<DisplayInfo> displays, Func<DisplayInfo, List<int>> ratesFor)
        {
            bool changed = false;
            DisplayInfo legacyTarget = _hasLegacy ? NativeDisplay.ResolveLegacy(displays, _legacyDisplay) : null;
            var main = NativeDisplay.MainDisplay(displays);
            bool anyInternal = displays.Any(d => d.IsInternal);
            bool noSettingsYet = Displays.Count == 0;

            foreach (var d in displays)
            {
                var existing = Get(d.Key);
                if (existing != null)
                {
                    if (!string.IsNullOrEmpty(d.Name) && existing.Name != d.Name) { existing.Name = d.Name; changed = true; }
                    if (existing.PluggedInHz <= 0 || existing.BatteryHz <= 0)
                    {
                        var late = ratesFor(d) ?? new List<int>();
                        if (late.Count > 0)
                        {
                            if (existing.PluggedInHz <= 0) existing.PluggedInHz = late.Max();
                            if (existing.BatteryHz <= 0) existing.BatteryHz = SuggestedBattery(late);
                            changed = true;
                        }
                    }
                    continue;
                }

                var rates = ratesFor(d) ?? new List<int>();
                var ds = new DisplaySettings
                {
                    Key = d.Key,
                    Name = d.Name,
                    PluggedInHz = rates.Count > 0 ? rates.Max() : 0,
                    BatteryHz = rates.Count > 0 ? SuggestedBattery(rates) : 0,
                    Port = d.IsEdidExternal ? d.Port : "",
                };

                if (legacyTarget != null && d == legacyTarget)
                {
                    ds.Managed = true;
                    if (_legacyPluggedIn > 0) ds.PluggedInHz = _legacyPluggedIn;
                    if (_legacyBattery > 0) ds.BatteryHz = _legacyBattery;
                }
                else if (_hasLegacy)
                    ds.Managed = false;   // 1.1.x only ever controlled one display
                else
                    ds.Managed = d.IsInternal || (!anyInternal && d == main && noSettingsYet);

                Displays[d.Key] = ds;
                changed = true;
            }
            if (legacyTarget != null) _hasLegacy = false;   // migrated
            return changed;
        }

        // ---------- Start with Windows (per-user Run key, no admin needed) ----------
        static string StartupCommand => "\"" + Application.ExecutablePath + "\" " + BackgroundArg;

        public static bool StartupEnabled
        {
            get
            {
                var v = RunValue();
                return !string.IsNullOrEmpty(v) &&
                       v.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (value) k.SetValue(RunValueName, StartupCommand);
                    else k.DeleteValue(RunValueName, false);
                }
            }
        }

        /// <summary>
        /// True if a Run entry exists but is out of date: it points at a different path (exe was moved)
        /// or lacks --background (written by 1.1.x, would open the settings window at every sign-in).
        /// </summary>
        public static bool StartupEntryNeedsRepair
        {
            get
            {
                var v = RunValue();
                return !string.IsNullOrEmpty(v) &&
                       (v.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) < 0 ||
                        v.IndexOf(BackgroundArg, StringComparison.OrdinalIgnoreCase) < 0);
            }
        }

        static string RunValue()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                return k?.GetValue(RunValueName) as string;
        }
    }
}
