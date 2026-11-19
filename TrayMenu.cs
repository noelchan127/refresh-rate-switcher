// Refresh Rate Switcher
// Copyright (C) 2026 noelchan127
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RefreshRateSwitcher
{
    /// <summary>Builds the tray icon's right-click menu. Rebuilt each time it opens, so it's always current.</summary>
    internal static class TrayMenu
    {
        public static void Build(ContextMenuStrip menu, IAppHost host)
        {
            // Dispose the previous items so their handles/images don't accumulate
            var old = menu.Items.Cast<ToolStripItem>().ToList();
            menu.Items.Clear();
            foreach (var it in old) it.Dispose();

            if (!(menu.Tag is Font bold)) menu.Tag = bold = new Font(menu.Font, FontStyle.Bold);

            var s = host.Settings;
            var displays = host.Displays();
            bool ac = host.OnAC;
            var main = NativeDisplay.MainDisplay(displays);

            string headerText = displays.Count <= 1
                ? $"{AppInfo.StateName(ac)} · now {(main != null ? main.CurrentHz : 0)} Hz"
                : $"{AppInfo.StateName(ac)} · {displays.Count} displays";
            menu.Items.Add(new ToolStripMenuItem(headerText) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());

            var settingsItem = new ToolStripMenuItem("Settings…") { Font = bold };
            settingsItem.Click += (o, e) => host.OpenSettings();
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());

            var enabled = new ToolStripMenuItem("Auto-switch enabled") { Checked = s.Enabled };
            enabled.Click += (o, e) => host.SetEnabled(!s.Enabled);
            menu.Items.Add(enabled);

            if (displays.Count == 1)
            {
                var d = displays[0];
                var ds = s.Get(d.Key);
                var rates = host.Rates(d);
                if (ds != null && !ds.Managed)
                {
                    var manage = new ToolStripMenuItem("Switch this display") { Checked = false };
                    manage.Click += (o, e) => host.SetManaged(d.Key, true);
                    menu.Items.Add(manage);
                }
                menu.Items.Add(RateMenu("Plugged in", true, d, ds, rates, ac, host, bold));
                menu.Items.Add(RateMenu("On battery", false, d, ds, rates, ac, host, bold));
            }
            else if (displays.Count > 1)
            {
                foreach (var d in displays)
                {
                    var ds = s.Get(d.Key);
                    bool managed = ds != null && ds.Managed;
                    var sub = new ToolStripMenuItem($"{d.Name} · {d.CurrentHz} Hz" + (managed ? "" : "  (not switched)"));
                    var manage = new ToolStripMenuItem("Switch this display") { Checked = managed };
                    string key = d.Key;
                    manage.Click += (o, e) => host.SetManaged(key, !managed);
                    sub.DropDownItems.Add(manage);
                    sub.DropDownItems.Add(new ToolStripSeparator());
                    var rates = host.Rates(d);
                    sub.DropDownItems.Add(RateMenu("Plugged in", true, d, ds, rates, ac, host, bold));
                    sub.DropDownItems.Add(RateMenu("On battery", false, d, ds, rates, ac, host, bold));
                    menu.Items.Add(sub);
                }
            }
            else
                menu.Items.Add(new ToolStripMenuItem("No displays found") { Enabled = false });

            menu.Items.Add(new ToolStripSeparator());

            bool startup = false;
            try { startup = host.StartupEnabled; } catch { }
            var startItem = new ToolStripMenuItem("Start with Windows") { Checked = startup };
            startItem.Click += (o, e) => host.StartupEnabled = !startup;
            menu.Items.Add(startItem);

            var notifItem = new ToolStripMenuItem("Show notifications") { Checked = s.Notifications };
            notifItem.Click += (o, e) => host.SetNotifications(!s.Notifications);
            menu.Items.Add(notifItem);

            menu.Items.Add(new ToolStripSeparator());
            var about = new ToolStripMenuItem("About");
            about.Click += (o, e) => host.OpenSettings(about: true);
            menu.Items.Add(about);
            var exit = new ToolStripMenuItem("Exit");
            exit.Click += (o, e) => host.Exit();
            menu.Items.Add(exit);
        }

        static ToolStripMenuItem RateMenu(string title, bool forAC, DisplayInfo d, DisplaySettings ds,
                                          List<int> rates, bool onAC, IAppHost host, Font bold)
        {
            int chosen = ds == null ? 0 : (forAC ? ds.PluggedInHz : ds.BatteryHz);
            int effective = NativeDisplay.Nearest(rates, chosen);
            var m = new ToolStripMenuItem($"{title}: {(chosen > 0 ? effective + " Hz" : "not set")}");
            if (forAC == onAC) m.Font = bold;   // highlight the active power state

            if (rates.Count == 0)
                m.DropDownItems.Add(new ToolStripMenuItem("No rates reported by driver") { Enabled = false });

            string key = d.Key;
            foreach (int hz in rates.OrderByDescending(r => r))
            {
                int value = hz;
                var it = new ToolStripMenuItem($"{hz} Hz" + (hz == d.CurrentHz ? "   (current)" : ""))
                { Checked = chosen > 0 && hz == effective };
                it.Click += (o, e) => host.SetRate(key, forAC, value);
                m.DropDownItems.Add(it);
            }
            return m;
        }
    }
}
