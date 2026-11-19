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
        public string DeviceName;   // GDI name, e.g. \\.\DISPLAY1 – can change between docks/reboots
        public string Key;          // stable ID used to store this display's settings
        public string Name;         // friendly name, e.g. "Built-in display" or "DELL U2723QE"
        public bool IsPrimary;
        public bool IsInternal;
        public int Width, Height, CurrentHz;

        // Raw identity data (from the CCD API) used to build Key
        public bool EdidValid;
        public ushort EdidManufacturer, EdidProduct;
        public uint Connector;
        public int OutputTech;
        public string MonitorPath = "";

        public string ShortDeviceName => (DeviceName ?? "").Replace(@"\\.\", "");

        public string Label
        {
            get
            {
                var tags = new List<string>();
                if (IsPrimary) tags.Add("primary");
                string t = tags.Count > 0 ? " (" + string.Join(", ", tags) + ")" : "";
                return $"{Name} – {Width}×{Height} @ {CurrentHz} Hz{t}";
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
        const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
        const uint TARGET_NAME_EDID_IDS_VALID = 0x4;
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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public int type; public int size; public LUID adapterId; public uint id;
            public uint flags;
            public int outputTechnology;
            public ushort edidManufactureId, edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint numModes, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topologyId);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int DisplayConfigGetTargetInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);

        /// <summary>Struct sizes as Windows expects them (checked by --selftest).</summary>
        internal static string StructSizeReport() =>
            $"DEVMODE={Marshal.SizeOf(typeof(DEVMODE))}/220 DISPLAY_DEVICE={Marshal.SizeOf(typeof(DISPLAY_DEVICE))}/840 " +
            $"PATH_INFO={Marshal.SizeOf(typeof(DISPLAYCONFIG_PATH_INFO))}/72 MODE_INFO={Marshal.SizeOf(typeof(DISPLAYCONFIG_MODE_INFO))}/64 " +
            $"SOURCE_NAME={Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME))}/84 TARGET_NAME={Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME))}/420";

        internal static bool StructSizesOk() =>
            Marshal.SizeOf(typeof(DEVMODE)) == 220 && Marshal.SizeOf(typeof(DISPLAY_DEVICE)) == 840 &&
            Marshal.SizeOf(typeof(DISPLAYCONFIG_PATH_INFO)) == 72 && Marshal.SizeOf(typeof(DISPLAYCONFIG_MODE_INFO)) == 64 &&
            Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME)) == 84 && Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME)) == 420;

        static DEVMODE NewDevMode() => new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };

        sealed class TargetInfo
        {
            public bool Internal, EdidValid;
            public ushort Mfg, Prod;
            public uint Connector;
            public int Tech;
            public string Friendly = "", Path = "";
        }

        static bool IsInternalTech(int tech) =>
            tech == OUTPUT_TECH_INTERNAL || tech == OUTPUT_TECH_DISPLAYPORT_EMBEDDED ||
            tech == OUTPUT_TECH_UDI_EMBEDDED || tech == OUTPUT_TECH_LVDS;

        /// <summary>For each GDI source (\\.\DISPLAYn), the monitor(s) it drives (more than one when duplicated).</summary>
        static Dictionary<string, List<TargetInfo>> GetTargetsBySource()
        {
            var result = new Dictionary<string, List<TargetInfo>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0) return result;
                var paths = new DISPLAYCONFIG_PATH_INFO[np];
                var modes = new DISPLAYCONFIG_MODE_INFO[nm];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return result;
                for (int i = 0; i < np; i++)
                {
                    var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME)),
                        adapterId = paths[i].source.adapterId,
                        id = paths[i].source.id
                    };
                    if (DisplayConfigGetDeviceInfo(ref src) != 0 || string.IsNullOrEmpty(src.viewGdiDeviceName)) continue;

                    var t = new TargetInfo { Tech = paths[i].target.outputTechnology };
                    var tgt = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME)),
                        adapterId = paths[i].target.adapterId,
                        id = paths[i].target.id
                    };
                    if (DisplayConfigGetTargetInfo(ref tgt) == 0)
                    {
                        t.Tech = tgt.outputTechnology;
                        t.EdidValid = (tgt.flags & TARGET_NAME_EDID_IDS_VALID) != 0;
                        t.Mfg = tgt.edidManufactureId;
                        t.Prod = tgt.edidProductCodeId;
                        t.Connector = tgt.connectorInstance;
                        t.Friendly = tgt.monitorFriendlyDeviceName ?? "";
                        t.Path = tgt.monitorDevicePath ?? "";
                    }
                    t.Internal = IsInternalTech(t.Tech);

                    if (!result.TryGetValue(src.viewGdiDeviceName, out var list))
                        result[src.viewGdiDeviceName] = list = new List<TargetInfo>();
                    list.Add(t);
                }
            }
            catch { /* CCD API unavailable – displays fall back to GDI names */ }
            return result;
        }

        public static List<DisplayInfo> GetDisplays()
        {
            var targets = GetTargetsBySource();
            var list = new List<DisplayInfo>();
            for (uint i = 0; ; i++)
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
                var dm = NewDevMode();
                if (!EnumDisplaySettings(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref dm)) continue;

                var info = new DisplayInfo
                {
                    DeviceName = dd.DeviceName,
                    IsPrimary = (dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                    Width = dm.dmPelsWidth,
                    Height = dm.dmPelsHeight,
                    CurrentHz = dm.dmDisplayFrequency
                };
                if (targets.TryGetValue(dd.DeviceName, out var ts) && ts.Count > 0)
                {
                    // Duplicated ("mirror") displays share one refresh rate; identify them by the first monitor
                    var main = ts.FirstOrDefault(x => x.Internal) ?? ts[0];
                    info.IsInternal = ts.Any(x => x.Internal);
                    info.EdidValid = main.EdidValid;
                    info.EdidManufacturer = main.Mfg;
                    info.EdidProduct = main.Prod;
                    info.Connector = main.Connector;
                    info.OutputTech = main.Tech;
                    info.MonitorPath = main.Path;
                    var names = ts.Select(x => x.Internal ? "Built-in display" : x.Friendly)
                                  .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
                    info.Name = names.Count > 0 ? string.Join(" + ", names) : null;
                }
                list.Add(info);
            }
            AssignKeys(list);
            return list;
        }

        /// <summary>
        /// Gives each display a stable ID so its settings follow the monitor rather than the
        /// \\.\DISPLAYn number (which can change with docks, GPU switching or reboots).
        ///  - built-in panel                      → "internal"
        ///  - external with EDID IDs              → "edid:MMMM-PPPP" (same monitor on any port keeps its settings)
        ///  - two identical external monitors     → "edid:MMMM-PPPP:tTcC" (told apart by connector)
        ///  - no EDID info                        → "path:..." or, failing that, "gdi:DISPLAYn"
        /// </summary>
        internal static void AssignKeys(List<DisplayInfo> list)
        {
            int internalCount = list.Count(d => d.IsInternal);
            foreach (var d in list)
            {
                if (d.IsInternal)
                    d.Key = internalCount == 1 ? "internal" : "internal:" + (d.EdidValid ? $"{d.EdidManufacturer:X4}-{d.EdidProduct:X4}:" : "") + d.ShortDeviceName;
                else if (d.EdidValid)
                {
                    string baseKey = $"edid:{d.EdidManufacturer:X4}-{d.EdidProduct:X4}";
                    bool dup = list.Count(o => !o.IsInternal && o.EdidValid &&
                                               o.EdidManufacturer == d.EdidManufacturer && o.EdidProduct == d.EdidProduct) > 1;
                    d.Key = dup ? $"{baseKey}:t{d.OutputTech}c{d.Connector}" : baseKey;
                }
                else if (!string.IsNullOrEmpty(d.MonitorPath))
                    d.Key = "path:" + d.MonitorPath;
                else
                    d.Key = "gdi:" + d.ShortDeviceName;

                if (string.IsNullOrWhiteSpace(d.Name))
                    d.Name = d.IsInternal ? "Built-in display" : "Display " + d.ShortDeviceName.Replace("DISPLAY", "");
            }
            // Two monitors of the same model: number them so they can be told apart in the menu and window
            foreach (var g in list.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                int n = 1;
                foreach (var d in g.OrderBy(x => x.Connector).ThenBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase))
                    d.Name += $" ({n++})";
            }

            // Last-resort uniqueness guard
            foreach (var g in list.GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var d in g.Skip(1)) d.Key += "#" + d.ShortDeviceName;
        }

        /// <summary>The display whose rate the tray icon shows: built-in panel, else primary, else first.</summary>
        public static DisplayInfo MainDisplay(List<DisplayInfo> displays) =>
            displays.FirstOrDefault(d => d.IsInternal) ?? displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();

        /// <summary>1.1.x setting: "auto" = built-in panel if one exists, else primary display; otherwise a GDI name.</summary>
        public static DisplayInfo ResolveLegacy(List<DisplayInfo> displays, string setting)
        {
            if (displays.Count == 0) return null;
            if (!string.IsNullOrEmpty(setting) && setting != "auto")
            {
                var match = displays.FirstOrDefault(d => d.DeviceName.Equals(setting, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return MainDisplay(displays);
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

        /// <summary>Asks the driver whether a rate would work, without changing anything (used by --selftest).</summary>
        public static string TestRate(string deviceName, int hz)
        {
            var dm = NewDevMode();
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm)) return "Could not read current display mode.";
            dm.dmDisplayFrequency = hz;
            dm.dmFields = DM_DISPLAYFREQUENCY;
            int test = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
            return test == 0 ? null : Describe(test);
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
