// Refresh Rate Switcher
// Copyright (C) 2026 Noel Chan
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Reflection;

namespace RefreshRateSwitcher
{
    /// <summary>What the tray menu and settings window need from the running app (faked by --selftest).</summary>
    internal interface IAppHost
    {
        Settings Settings { get; }
        bool OnAC { get; }
        List<DisplayInfo> Displays();
        List<int> Rates(DisplayInfo d);

        void SetEnabled(bool on);
        void SetManaged(string key, bool on);
        void SetRate(string key, bool forAC, int hz);
        void SetNotifications(bool on);
        bool StartupEnabled { get; set; }

        void OpenSettings(bool about = false);
        void Exit();
    }

    internal static class AppInfo
    {
        public const string Name = "Refresh Rate Switcher";
        public const string Author = "Noel Chan";
        public const string AuthorUrl = "https://github.com/noelchan127";
        public const string RepoUrl = "https://github.com/noelchan127/refresh-rate-switcher";
        public const string ReleasesUrl = RepoUrl + "/releases/latest";
        public const string LicenseUrl = RepoUrl + "/blob/main/LICENSE";
        public const string License = "GNU GPL v3.0 or later";

        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        public static string StateName(bool ac) => ac ? "Plugged in" : "On battery";
    }
}
