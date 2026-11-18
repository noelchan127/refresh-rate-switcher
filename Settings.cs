// Refresh Rate Switcher
// Copyright (C) 2026 noelchan127
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RefreshRateSwitcher
{
    /// <summary>Settings stored per-user in HKCU\Software\RefreshRateSwitcher.</summary>
    internal sealed class Settings
    {
        const string KeyPath = @"Software\RefreshRateSwitcher";
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValueName = "RefreshRateSwitcher";

        public bool Enabled = true;
        public int PluggedInHz;     // 0 = not chosen yet
        public int BatteryHz;       // 0 = not chosen yet
        public string Display = "auto";
        public bool Notifications = true;
        public bool FirstRun = true;

        public static Settings Load()
        {
            var s = new Settings();
            using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
            {
                if (k == null) return s;
                s.FirstRun = false;
                s.Enabled = Convert.ToInt32(k.GetValue("Enabled", 1)) != 0;
                s.PluggedInHz = Convert.ToInt32(k.GetValue("PluggedInHz", 0));
                s.BatteryHz = Convert.ToInt32(k.GetValue("BatteryHz", 0));
                s.Display = (k.GetValue("Display", "auto") as string) ?? "auto";
                s.Notifications = Convert.ToInt32(k.GetValue("Notifications", 1)) != 0;
            }
            return s;
        }

        public void Save()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                k.SetValue("Enabled", Enabled ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("PluggedInHz", PluggedInHz, RegistryValueKind.DWord);
                k.SetValue("BatteryHz", BatteryHz, RegistryValueKind.DWord);
                k.SetValue("Display", Display ?? "auto", RegistryValueKind.String);
                k.SetValue("Notifications", Notifications ? 1 : 0, RegistryValueKind.DWord);
            }
            FirstRun = false;
        }

        // ---------- Start with Windows (per-user Run key, no admin needed) ----------
        public static bool StartupEnabled
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    var v = k?.GetValue(RunValueName) as string;
                    return !string.IsNullOrEmpty(v) &&
                           v.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (value) k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunValueName, false);
                }
            }
        }

        /// <summary>True if a Run entry exists but points at a different path (exe was moved).</summary>
        public static bool StartupEntryStale
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    var v = k?.GetValue(RunValueName) as string;
                    return !string.IsNullOrEmpty(v) &&
                           v.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) < 0;
                }
            }
        }
    }
}
