// Refresh Rate Switcher
// Copyright (C) 2026 Noel Chan
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace RefreshRateSwitcher
{
    /// <summary>
    /// Reuses display and refresh-rate queries within one event. Listing the displays takes about
    /// 10 ms and each display's rate list is a walk over every mode the driver offers (hundreds on a
    /// real GPU). One display change used to repeat these up to 7 times (tray icon, settings window,
    /// then the re-apply), and our own rate change triggers another round.
    ///  - The display list is reused for at most MaxAgeMs, and dropped at once on any display change.
    ///  - Rate lists are kept per display and resolution until the next display change. An empty list
    ///    (driver hasn't reported its rates yet) is never kept, so it's asked again next time.
    /// Callers must treat the returned lists as read-only: they're shared.
    /// </summary>
    internal sealed class DisplayCache
    {
        public const int MaxAgeMs = 1000;

        readonly Func<List<DisplayInfo>> _queryDisplays;
        readonly Func<DisplayInfo, List<int>> _queryRates;
        readonly Func<int> _now;
        readonly Dictionary<string, List<int>> _rates = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        List<DisplayInfo> _displays;
        int _displaysAt;

        public DisplayCache(Func<List<DisplayInfo>> queryDisplays, Func<DisplayInfo, List<int>> queryRates, Func<int> now = null)
        {
            _queryDisplays = queryDisplays;
            _queryRates = queryRates;
            _now = now ?? (() => Environment.TickCount);
        }

        public List<DisplayInfo> Displays()
        {
            int now = _now();
            if (_displays == null || unchecked(now - _displaysAt) >= MaxAgeMs || unchecked(now - _displaysAt) < 0)
            {
                _displays = _queryDisplays();
                _displaysAt = now;
            }
            return _displays;
        }

        public List<int> Rates(DisplayInfo d)
        {
            string key = $"{d.DeviceName}|{d.Width}x{d.Height}";
            if (_rates.TryGetValue(key, out var list)) return list;
            list = _queryRates(d) ?? new List<int>();
            if (list.Count > 0) _rates[key] = list;
            return list;
        }

        /// <summary>Call on any display change, wake from sleep, or after changing a display's mode.</summary>
        public void Invalidate()
        {
            _displays = null;
            _rates.Clear();
        }

        /// <summary>Only the current mode changed (e.g. after our own SetRate): rate lists stay valid.</summary>
        public void InvalidateDisplays() => _displays = null;
    }
}
