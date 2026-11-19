// Refresh Rate Switcher
// Copyright (C) 2026 noelchan127
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RefreshRateSwitcher
{
    /// <summary>
    /// "RefreshRateSwitcher.exe --selftest &lt;dir&gt;" – checks the Windows API calls on the real machine,
    /// the settings/migration logic, and the window layout with simulated multi-monitor setups.
    /// Never changes the display mode or the real settings. Writes selftest.txt and PNG previews to &lt;dir&gt;.
    /// </summary>
    internal static class SelfTest
    {
        static StringBuilder _log;
        static int _fails;

        static void Check(bool ok, string what)
        {
            _log.AppendLine((ok ? "PASS  " : "FAIL  ") + what);
            if (!ok) _fails++;
        }

        static void Info(string text) => _log.AppendLine("      " + text);

        public static int Run(string dir)
        {
            Directory.CreateDirectory(dir);
            _log = new StringBuilder();
            _fails = 0;
            _log.AppendLine($"{AppInfo.Name} {AppInfo.Version} self-test, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _log.AppendLine("OS: " + Environment.OSVersion + (Environment.Is64BitProcess ? " (64-bit)" : " (32-bit)"));
            _log.AppendLine();

            var steps = new (string name, Action<string> run)[]
            {
                ("Windows display APIs", d => RealDisplays()),
                ("Display IDs", d => KeyAssignment()),
                ("Nearest rate", d => NearestRate()),
                ("Settings & migration", d => SettingsTests()),
                ("Tray menu", d => MenuTests()),
                ("Settings window", WindowTests),
            };
            foreach (var (name, run) in steps)
            {
                _log.AppendLine("== " + name);
                try { run(dir); }
                catch (Exception ex) { Check(false, "exception: " + ex); }
                _log.AppendLine();
            }

            _log.AppendLine(_fails == 0 ? "ALL PASSED" : $"{_fails} FAILURE(S)");
            File.WriteAllText(Path.Combine(dir, "selftest.txt"), _log.ToString());
            return _fails == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ real hardware

        static void RealDisplays()
        {
            Check(NativeDisplay.StructSizesOk(), "struct sizes " + NativeDisplay.StructSizeReport());
            var sw = Stopwatch.StartNew();
            var displays = NativeDisplay.GetDisplays();
            Info($"GetDisplays took {sw.ElapsedMilliseconds} ms");
            Check(displays.Count > 0, $"found {displays.Count} display(s)");
            Check(displays.Select(d => d.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == displays.Count, "display IDs are unique");
            foreach (var d in displays)
            {
                Info($"{d.ShortDeviceName}: '{d.Name}' id={d.Key} internal={d.IsInternal} primary={d.IsPrimary} " +
                     $"{d.Width}x{d.Height}@{d.CurrentHz} tech={d.OutputTech} edid={d.EdidValid}");
                sw.Restart();
                var rates = NativeDisplay.GetSupportedRates(d.DeviceName);
                Info($"rates [{string.Join(", ", rates)}] in {sw.ElapsedMilliseconds} ms");
                Check(rates.Count > 0, $"{d.ShortDeviceName} reports refresh rates");
                Check(rates.Contains(d.CurrentHz), $"{d.ShortDeviceName} current rate {d.CurrentHz} Hz is in its list");
                Check(NativeDisplay.GetCurrentRate(d.DeviceName) == d.CurrentHz, $"{d.ShortDeviceName} GetCurrentRate matches");
                var t = NativeDisplay.TestRate(d.DeviceName, d.CurrentHz);
                Check(t == null, $"{d.ShortDeviceName} driver accepts its current rate (CDS_TEST){(t == null ? "" : ": " + t)}");
                Check(NativeDisplay.SetRate(d.DeviceName, d.CurrentHz) == null, $"{d.ShortDeviceName} SetRate to the current rate is a no-op");
            }
            Check(NativeDisplay.MainDisplay(displays) != null, "a main display is chosen for the tray icon");
        }

        // ------------------------------------------------------------------ simulated setups

        const string LgPath = @"\\?\DISPLAY#GSM5B7F#5&1a2b3c&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

        // A laptop with its built-in panel, two identical Dell monitors and a monitor without EDID IDs
        static List<DisplayInfo> Laptop() => new List<DisplayInfo>
        {
            new DisplayInfo { DeviceName = @"\\.\DISPLAY1", Name = "Built-in display", IsInternal = true, IsPrimary = true,
                              Width = 2560, Height = 1600, CurrentHz = 165, EdidValid = true, EdidManufacturer = 0xE430, EdidProduct = 0x0A1F, OutputTech = 11 },
            new DisplayInfo { DeviceName = @"\\.\DISPLAY2", Name = "DELL U2723QE", Width = 3840, Height = 2160, CurrentHz = 60,
                              EdidValid = true, EdidManufacturer = 0xAC10, EdidProduct = 0xA0B1, OutputTech = 10, Connector = 0 },
            new DisplayInfo { DeviceName = @"\\.\DISPLAY3", Name = "DELL U2723QE", Width = 3840, Height = 2160, CurrentHz = 60,
                              EdidValid = true, EdidManufacturer = 0xAC10, EdidProduct = 0xA0B1, OutputTech = 10, Connector = 1 },
            new DisplayInfo { DeviceName = @"\\.\DISPLAY4", Name = "LG ULTRAGEAR", Width = 2560, Height = 1440, CurrentHz = 144,
                              EdidValid = false, MonitorPath = LgPath, OutputTech = 5 },
        };

        static List<int> FakeRates(DisplayInfo d)
        {
            if (d.IsInternal) return new List<int> { 48, 60, 120, 165 };
            if (d.Name.StartsWith("DELL")) return new List<int> { 30, 50, 59, 60 };
            return new List<int> { 60, 100, 120, 144 };
        }

        static void KeyAssignment()
        {
            var l = Laptop();
            NativeDisplay.AssignKeys(l);
            Check(l[0].Key == "internal", "built-in panel → " + l[0].Key);
            Check(l[1].Key == "edid:AC10-A0B1:t10c0" && l[2].Key == "edid:AC10-A0B1:t10c1",
                  $"identical monitors told apart → {l[1].Key}, {l[2].Key}");
            Check(l[3].Key == "path:" + LgPath, "monitor without EDID IDs uses its device path");

            var single = new List<DisplayInfo> { Laptop()[1] };
            NativeDisplay.AssignKeys(single);
            Check(single[0].Key == "edid:AC10-A0B1", "single monitor keyed by model, so it keeps settings on any port");

            var noCcd = new List<DisplayInfo> { new DisplayInfo { DeviceName = @"\\.\DISPLAY7" } };
            NativeDisplay.AssignKeys(noCcd);
            Check(noCcd[0].Key == "gdi:DISPLAY7" && noCcd[0].Name == "Display 7", "fallback when Windows gives no monitor info");

            var clash = new List<DisplayInfo> { new DisplayInfo { DeviceName = @"\\.\DISPLAY1", MonitorPath = "x" },
                                                new DisplayInfo { DeviceName = @"\\.\DISPLAY2", MonitorPath = "x" } };
            NativeDisplay.AssignKeys(clash);
            Check(clash[0].Key != clash[1].Key, "duplicate IDs are made unique");
        }

        static void NearestRate()
        {
            var r = new List<int> { 48, 60, 120, 165 };
            Check(NativeDisplay.Nearest(r, 60) == 60, "exact match");
            Check(NativeDisplay.Nearest(r, 144) == 165, "144 → 165 on a panel without 144");
            Check(NativeDisplay.Nearest(r, 90) == 120, "tie 60/120 prefers higher");
            Check(NativeDisplay.Nearest(new List<int>(), 75) == 75, "no rate list → keeps wanted value");
        }

        static void SettingsTests()
        {
            string original = Settings.KeyPath;
            Settings.KeyPath = @"Software\RefreshRateSwitcher-SelfTest";
            try
            {
                void Wipe() => Registry.CurrentUser.DeleteSubKeyTree(Settings.KeyPath, false);
                var laptop = Laptop(); NativeDisplay.AssignKeys(laptop);

                // Fresh install
                Wipe();
                var s = Settings.Load();
                Check(s.FirstRun, "fresh install is detected as first run");
                Check(s.EnsureDisplays(laptop, FakeRates), "new displays are added");
                var i = s.Get("internal");
                Check(i != null && i.Managed && i.PluggedInHz == 165 && i.BatteryHz == 60, "built-in panel: switched, 165 Hz plugged in / 60 Hz battery");
                Check(laptop.Skip(1).All(d => s.Get(d.Key) != null && !s.Get(d.Key).Managed), "external monitors are left alone by default");
                Check(s.Get(laptop[1].Key).BatteryHz == 60 && s.Get(laptop[3].Key).PluggedInHz == 144, "external monitors get suggested rates ready");
                Check(!s.EnsureDisplays(laptop, FakeRates), "nothing re-added on second check");
                s.Save();

                var r = Settings.Load();
                Check(!r.FirstRun && r.Displays.Count == 4, "settings saved and reloaded (4 displays)");
                Check(r.Get(laptop[3].Key) != null, "ID containing backslashes survives the registry round trip");
                Check(r.Get("internal").PluggedInHz == 165, "values reloaded intact");

                // Upgrade from 1.1.x
                Wipe();
                using (var k = Registry.CurrentUser.CreateSubKey(Settings.KeyPath))
                {
                    k.SetValue("Enabled", 1); k.SetValue("PluggedInHz", 120); k.SetValue("BatteryHz", 48);
                    k.SetValue("Display", "auto"); k.SetValue("Notifications", 0);
                }
                s = Settings.Load();
                s.EnsureDisplays(laptop, FakeRates);
                i = s.Get("internal");
                Check(!s.FirstRun && !s.Notifications, "1.1.x general settings kept");
                Check(i.Managed && i.PluggedInHz == 120 && i.BatteryHz == 48, "1.1.x rates moved to the display it controlled (120/48)");
                Check(laptop.Skip(1).All(d => !s.Get(d.Key).Managed), "1.1.x upgrade leaves other displays alone");
                s.Save();
                s = Settings.Load();
                Check(s.Get("internal").PluggedInHz == 120, "migrated settings persist");

                // 1.1.x user who had picked a specific display
                Wipe();
                using (var k = Registry.CurrentUser.CreateSubKey(Settings.KeyPath))
                {
                    k.SetValue("PluggedInHz", 144); k.SetValue("BatteryHz", 60); k.SetValue("Display", @"\\.\DISPLAY4");
                }
                s = Settings.Load();
                s.EnsureDisplays(laptop, FakeRates);
                Check(s.Get(laptop[3].Key).Managed && !s.Get("internal").Managed, "1.1.x specific-display choice migrated to that display");

                // Desktop PC (no built-in panel): primary monitor is switched, others left alone
                Wipe();
                var desk = Laptop().Skip(1).ToList();
                desk[1].IsPrimary = true;
                NativeDisplay.AssignKeys(desk);
                s = Settings.Load();
                s.EnsureDisplays(desk, FakeRates);
                Check(s.Get(desk[1].Key).Managed && !s.Get(desk[0].Key).Managed && !s.Get(desk[2].Key).Managed,
                      "desktop: only the primary monitor is switched by default");
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(Settings.KeyPath, false); } catch { }
                Settings.KeyPath = original;
            }
        }

        // ------------------------------------------------------------------ UI

        sealed class FakeHost : IAppHost
        {
            readonly List<DisplayInfo> _displays;
            public FakeHost(List<DisplayInfo> displays, bool ac)
            {
                _displays = displays;
                NativeDisplay.AssignKeys(_displays);
                OnAC = ac;
                Settings = new Settings();
                Settings.EnsureDisplays(_displays, FakeRates);
            }
            public Settings Settings { get; }
            public bool OnAC { get; set; }
            public List<DisplayInfo> Displays() => _displays;
            public List<int> Rates(DisplayInfo d) => FakeRates(d);
            public void SetEnabled(bool on) => Settings.Enabled = on;
            public void SetManaged(string key, bool on) => Settings.Get(key).Managed = on;
            public void SetRate(string key, bool forAC, int hz) { var d = Settings.Get(key); if (forAC) d.PluggedInHz = hz; else d.BatteryHz = hz; }
            public void SetNotifications(bool on) => Settings.Notifications = on;
            public bool StartupEnabled { get; set; } = true;
            public void OpenSettings(bool about = false) { }
            public void Exit() { }
        }

        static List<string> MenuTexts(ToolStripItemCollection items, string indent = "")
        {
            var list = new List<string>();
            foreach (ToolStripItem it in items)
            {
                if (it is ToolStripSeparator) { list.Add(indent + "----"); continue; }
                var mi = it as ToolStripMenuItem;
                list.Add(indent + (mi != null && mi.Checked ? "[x] " : "") + it.Text + (it.Enabled ? "" : " (disabled)"));
                if (mi != null && mi.DropDownItems.Count > 0) list.AddRange(MenuTexts(mi.DropDownItems, indent + "    "));
            }
            return list;
        }

        static void MenuTests()
        {
            using (var menu = new ContextMenuStrip())
            {
                var one = new FakeHost(new List<DisplayInfo> { Laptop()[0] }, ac: false);
                TrayMenu.Build(menu, one);
                var t = MenuTexts(menu.Items);
                foreach (var line in t) Info(line);
                Check(t.Contains("Settings…") && t.Contains("About") && t.Contains("Exit"), "single display: Settings, About and Exit present");
                Check(t.Any(x => x.StartsWith("Plugged in: 165 Hz")) && t.Any(x => x.StartsWith("On battery: 60 Hz")), "single display: rate menus at top level");
                Check(t.Any(x => x.Trim() == "[x] 60 Hz"), "battery rate is ticked");

                var many = new FakeHost(Laptop(), ac: true);
                TrayMenu.Build(menu, many);   // rebuild on the same menu, as the app does
                t = MenuTexts(menu.Items);
                Info("");
                foreach (var line in t) Info(line);
                Check(t.Count(x => !x.StartsWith(" ") && (x.Contains("Built-in display ·") || x.Contains("DELL") || x.Contains("LG"))) == 4,
                      "multiple displays: one submenu per display");
                Check(t.Any(x => x.Contains("DELL U2723QE · 60 Hz  (not switched)")), "external monitor shows as not switched");
            }
        }

        static void WindowTests(string dir)
        {
            foreach (float scale in new[] { 1f, 1.5f })
            {
                foreach (bool about in new[] { false, true })
                {
                    var host = scale == 1f && !about ? new FakeHost(new List<DisplayInfo> { Laptop()[0] }, ac: false)
                                                     : new FakeHost(Laptop(), ac: true);
                    long before = GC.GetTotalMemory(true);
                    using (var f = new SettingsForm(host, about, scale))
                    {
                        f.StartPosition = FormStartPosition.Manual;
                        f.Location = new Point(-20000, -20000);
                        f.ShowInTaskbar = false;
                        f.Show();
                        Application.DoEvents();
                        string name = $"window-{(about ? "about" : "settings")}-{(host.Displays().Count == 1 ? "1display" : "4displays")}-{(int)(scale * 100)}pct.png";
                        using (var bmp = new Bitmap(f.Width, f.Height))
                        {
                            f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                            bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
                        }
                        Info($"{name}: {f.Width}x{f.Height} px");
                        var clipped = new List<string>();
                        FindClipped(f, clipped);
                        Check(clipped.Count == 0, $"{name}: no clipped controls" + (clipped.Count > 0 ? " → " + string.Join("; ", clipped) : ""));
                        var wa = Screen.PrimaryScreen.WorkingArea;
                        Info($"screen working area {wa.Width}x{wa.Height}");
                        if (scale == 1f && about == false)
                            Check(f.Height <= 768 && f.Width <= 1024, "window fits a 1366×768 laptop screen at 100%");

                        // Exercise the controls like a user would
                        if (!about)
                        {
                            var combos = AllControls(f).OfType<ComboBox>().ToList();
                            var checks = AllControls(f).OfType<CheckBox>().ToList();
                            Check(combos.Count == host.Displays().Count * 2, $"{combos.Count} rate drop-downs for {host.Displays().Count} display(s)");
                            var first = combos[0];
                            first.SelectedIndex = first.Items.Count - 1;   // lowest rate
                            var key = host.Displays()[0].Key;
                            Check(host.Settings.Get(key).PluggedInHz == FakeRates(host.Displays()[0]).Min(), "picking a rate in the window updates the setting");
                            var manage = checks.FirstOrDefault(c => c.Text.StartsWith("Switch this display") && !c.Checked);
                            if (manage != null)
                            {
                                manage.Checked = true;
                                Check(host.Settings.Displays.Values.Count(d => d.Managed) >= 2, "ticking 'Switch this display' enables it");
                            }
                            f.RefreshState();
                            Check(true, "RefreshState after changes");
                        }
                        f.Close();
                    }
                    Info($"managed memory delta {(GC.GetTotalMemory(true) - before) / 1024} KB after closing");
                }
            }
            Check(SettingsForm.DiagnosticText(new FakeHost(Laptop(), true)).Contains("rates: 48, 60, 120, 165"), "system info text for bug reports");
        }

        static IEnumerable<Control> AllControls(Control c)
        {
            foreach (Control child in c.Controls)
            {
                yield return child;
                foreach (var g in AllControls(child)) yield return g;
            }
        }

        // A control is clipped if it doesn't fit inside its parent's visible area,
        // or its text needs more room than it was given
        static void FindClipped(Control parent, List<string> found)
        {
            foreach (Control c in parent.Controls)
            {
                if (!c.Visible) continue;
                var area = parent.DisplayRectangle;
                if (parent is TabPage || parent is Form) area = new Rectangle(Point.Empty, parent.ClientSize);
                if (c.Right > area.Right + 1 || c.Bottom > area.Bottom + 1)
                    found.Add($"{c.GetType().Name} '{Trim(c.Text)}' {c.Bounds} outside {parent.GetType().Name} {area}");
                if ((c is Label || c is CheckBox || c is Button) && c.AutoSize)
                {
                    var pref = c.GetPreferredSize(new Size(c.MaximumSize.Width > 0 ? c.MaximumSize.Width : 0, 0));
                    if (pref.Width > c.Width + 1 || pref.Height > c.Height + 1)
                        found.Add($"{c.GetType().Name} '{Trim(c.Text)}' needs {pref} has {c.Size}");
                }
                if (c is TabControl tc)
                    foreach (TabPage p in tc.TabPages) FindClipped(p, found);
                else
                    FindClipped(c, found);
            }
        }

        static string Trim(string s) => s == null ? "" : (s.Length > 30 ? s.Substring(0, 30) + "…" : s);
    }
}
