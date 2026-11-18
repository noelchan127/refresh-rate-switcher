// Refresh Rate Switcher
// Copyright (C) 2026 noelchan127
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace RefreshRateSwitcher
{
    internal sealed class DisplayInfo
    {
        public string DeviceName;   // e.g. \\.\DISPLAY1
        public bool IsPrimary;
        public bool IsInternal;
        public int Width, Height, CurrentHz;

        public string Label
        {
            get
            {
                string n = DeviceName.Replace(@"\\.\", "");
                var tags = new List<string>();
                if (IsInternal) tags.Add("built-in");
                if (IsPrimary) tags.Add("primary");
                string t = tags.Count > 0 ? " (" + string.Join(", ", tags) + ")" : "";
                return $"{n} – {Width}×{Height} @ {CurrentHz} Hz{t}";
            }
        }
    }

    internal static class NativeDisplay
    {
        // ---------------- user32: DEVMODE / display settings ----------------
        const int ENUM_CURRENT_SETTINGS = -1;
        const int DM_DISPLAYFREQUENCY = 0x400000;
        const int CDS_UPDATEREGISTRY = 0x01;
        const int CDS_TEST = 0x02;
        const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
        const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY;
            public int dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE dd, uint flags);

        // ---------------- user32: CCD API (to find the built-in panel) ----------------
        const uint QDC_ONLY_ACTIVE_PATHS = 2;
        const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
        const int OUTPUT_TECH_LVDS = 6;
        const int OUTPUT_TECH_DISPLAYPORT_EMBEDDED = 11;
        const int OUTPUT_TECH_UDI_EMBEDDED = 13;
        const int OUTPUT_TECH_INTERNAL = unchecked((int)0x80000000);

        [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx;
            public int outputTechnology, rotation, scaling;
            public uint refreshNum, refreshDen;
            public int scanLineOrdering; public int targetAvailable; public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO source; public DISPLAYCONFIG_PATH_TARGET_INFO target; public uint flags; }

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        struct DISPLAYCONFIG_MODE_INFO { public int infoType; public uint id; public LUID adapterId; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public int type; public int size; public LUID adapterId; public uint id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint numModes, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topologyId);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);

        static DEVMODE NewDevMode() => new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };

        /// <summary>GDI device names (\\.\DISPLAYn) that are driven by an internal/embedded panel.</summary>
        static HashSet<string> GetInternalDeviceNames()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0) return result;
                var paths = new DISPLAYCONFIG_PATH_INFO[np];
                var modes = new DISPLAYCONFIG_MODE_INFO[nm];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return result;
                for (int i = 0; i < np; i++)
                {
                    int tech = paths[i].target.outputTechnology;
                    bool internalPanel = tech == OUTPUT_TECH_INTERNAL || tech == OUTPUT_TECH_DISPLAYPORT_EMBEDDED ||
                                         tech == OUTPUT_TECH_UDI_EMBEDDED || tech == OUTPUT_TECH_LVDS;
                    if (!internalPanel) continue;
                    var req = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME)),
                        adapterId = paths[i].source.adapterId,
                        id = paths[i].source.id
                    };
                    if (DisplayConfigGetDeviceInfo(ref req) == 0 && !string.IsNullOrEmpty(req.viewGdiDeviceName))
                        result.Add(req.viewGdiDeviceName);
                }
            }
            catch { /* CCD API unavailable – fall back to primary */ }
            return result;
        }

        public static List<DisplayInfo> GetDisplays()
        {
            var internals = GetInternalDeviceNames();
            var list = new List<DisplayInfo>();
            for (uint i = 0; ; i++)
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
                var dm = NewDevMode();
                if (!EnumDisplaySettings(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref dm)) continue;
                list.Add(new DisplayInfo
                {
                    DeviceName = dd.DeviceName,
                    IsPrimary = (dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                    IsInternal = internals.Contains(dd.DeviceName),
                    Width = dm.dmPelsWidth,
                    Height = dm.dmPelsHeight,
                    CurrentHz = dm.dmDisplayFrequency
                });
            }
            return list;
        }

        /// <summary>"auto" = built-in panel if one exists, else primary display.</summary>
        public static DisplayInfo Resolve(string setting)
        {
            var displays = GetDisplays();
            if (displays.Count == 0) return null;
            if (!string.IsNullOrEmpty(setting) && setting != "auto")
            {
                var match = displays.FirstOrDefault(d => d.DeviceName.Equals(setting, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return displays.FirstOrDefault(d => d.IsInternal)
                ?? displays.FirstOrDefault(d => d.IsPrimary)
                ?? displays[0];
        }

        /// <summary>Refresh rates supported at the display's current resolution and colour depth.</summary>
        public static List<int> GetSupportedRates(string deviceName)
        {
            var cur = NewDevMode();
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref cur)) return new List<int>();
            var rates = new SortedSet<int>();
            for (int i = 0; ; i++)
            {
                var dm = NewDevMode();
                if (!EnumDisplaySettings(deviceName, i, ref dm)) break;
                if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight &&
                    dm.dmBitsPerPel == cur.dmBitsPerPel && dm.dmDisplayFrequency > 1)
                    rates.Add(dm.dmDisplayFrequency);
            }
            return rates.ToList();
        }

        public static int GetCurrentRate(string deviceName)
        {
            var dm = NewDevMode();
            return EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm) ? dm.dmDisplayFrequency : 0;
        }

        /// <summary>Returns null on success, otherwise an error description.</summary>
        public static string SetRate(string deviceName, int hz)
        {
            var dm = NewDevMode();
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm)) return "Could not read current display mode.";
            if (dm.dmDisplayFrequency == hz) return null;
            dm.dmDisplayFrequency = hz;
            dm.dmFields = DM_DISPLAYFREQUENCY;

            int test = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
            if (test != 0) return Describe(test);
            int r = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            return r == 0 ? null : Describe(r);
        }

        static string Describe(int code)
        {
            switch (code)
            {
                case 1: return "A restart is required for the change to take effect.";
                case -1: return "The display driver failed the requested mode.";
                case -2: return "The refresh rate is not supported at the current resolution.";
                case -3: return "Unable to write settings to the registry.";
                case -4: return "Invalid flags.";
                case -5: return "Invalid parameter.";
                case -6: return "Display settings are locked by another app or policy.";
                default: return "Display change failed (code " + code + ").";
            }
        }

        /// <summary>Pick the supported rate closest to the wanted one (prefers exact match).</summary>
        public static int Nearest(List<int> rates, int wanted)
        {
            if (rates.Count == 0) return wanted;
            return rates.OrderBy(r => Math.Abs(r - wanted)).ThenByDescending(r => r).First();
        }
    }
}
