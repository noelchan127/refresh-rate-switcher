// Refresh Rate Switcher
// Copyright (C) 2026 noelchan127
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Windows.Forms;

namespace RefreshRateSwitcher
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // --uninstall removes the startup entry and saved settings (handy before deleting the exe)
            if (args.Length > 0 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                try { Settings.StartupEnabled = false; } catch { }
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\RefreshRateSwitcher", false); } catch { }
                MessageBox.Show("Startup entry and settings removed.", "Refresh Rate Switcher");
                return;
            }

            using (var mutex = new Mutex(true, @"Local\RefreshRateSwitcher_SingleInstance", out bool isNew))
            {
                if (!isNew)
                {
                    // Already running (e.g. launched again from the Start Menu): ask that copy to pop up its menu
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(TrayApp.ShowMenuEventName)) ev.Set();
                    }
                    catch
                    {
                        MessageBox.Show("Refresh Rate Switcher is already running.\nLook for its icon in the system tray.",
                            "Refresh Rate Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}
