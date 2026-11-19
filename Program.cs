// Refresh Rate Switcher
// Copyright (C) 2026 noelchan127
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RefreshRateSwitcher
{
    internal static class Program
    {
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
        const int ASFW_ANY = -1;

        // Command line:
        //   (none)              start (or bring up the running copy) and open the settings window
        //   --background        start in the tray only – used by the "Start with Windows" entry
        //   --uninstall         remove the startup entry and saved settings
        //   --selftest <dir>    run built-in checks, write results to <dir> and exit (used by CI)
        [STAThread]
        static int Main(string[] args)
        {
            bool Has(string a) => args.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));

            if (Has("--uninstall"))
            {
                try { Settings.StartupEnabled = false; } catch { }
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Settings.KeyPath, false); } catch { }
                MessageBox.Show("Startup entry and settings removed.", AppInfo.Name);
                return 0;
            }

            if (Has("--selftest"))
            {
                int i = Array.FindIndex(args, x => x.Equals("--selftest", StringComparison.OrdinalIgnoreCase));
                string dir = i + 1 < args.Length ? args[i + 1] : ".";
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                return SelfTest.Run(dir);
            }

            bool background = Has(Settings.BackgroundArg);

            using (var mutex = new Mutex(true, @"Local\RefreshRateSwitcher_SingleInstance", out bool isNew))
            {
                if (!isNew)
                {
                    if (background) return 0;   // already running; nothing to show
                    // Ask the running copy to open its window, and let it come to the front
                    try
                    {
                        AllowSetForegroundWindow(ASFW_ANY);
                        using (var ev = EventWaitHandle.OpenExisting(TrayApp.ShowMenuEventName)) ev.Set();
                    }
                    catch
                    {
                        MessageBox.Show("Refresh Rate Switcher is already running.\nLook for its icon in the system tray.",
                            AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // An unexpected error shouldn't take the app down or pop up a .NET error box:
                // note it in %TEMP%\RefreshRateSwitcher-errors.log and carry on
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => LogError(e.Exception);
                Application.Run(new TrayApp(background));
            }
            return 0;
        }

        internal static void LogError(Exception ex)
        {
            try
            {
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RefreshRateSwitcher-errors.log");
                var info = new System.IO.FileInfo(path);
                if (info.Exists && info.Length > 256 * 1024) info.Delete();   // keep it small
                System.IO.File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {AppInfo.Name} {AppInfo.Version}\r\n{ex}\r\n\r\n");
            }
            catch { }
        }
    }
}
