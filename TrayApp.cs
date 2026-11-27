// Refresh Rate Switcher
// Copyright (C) 2026 Noel Chan
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RefreshRateSwitcher
{
    internal sealed class TrayApp : ApplicationContext, IAppHost
    {
        readonly NotifyIcon _tray;
        readonly ContextMenuStrip _menu;
        readonly Settings _settings;
        readonly System.Windows.Forms.Timer _debounce;
        string _pendingReason = "";
        Icon _currentIcon;
        SettingsForm _form;

        readonly ApplyCache _cache = new ApplyCache();
        readonly DisplayCache _displayCache;
        (int hz, int argb, int size) _iconDrawn;   // what the tray icon currently shows
        int _retries;   // follow-up passes scheduled for failed changes or rates not reported yet

        // Power source the rates were last applied for. Battery-percentage notifications arrive as
        // the same event as plugging in or unplugging; only a real change re-applies the rates.
        bool _lastAC;

        // Signalled by a second launch (e.g. Start Menu shortcut) to open this instance's window
        public const string ShowMenuEventName = @"Local\RefreshRateSwitcher_ShowMenu";
        readonly EventWaitHandle _showEvent;
        readonly RegisteredWaitHandle _showWait;

        // Signalled by the installer/uninstaller to ask this copy to exit cleanly (installer.nsi uses the same name)
        public const string ExitEventName = @"Local\RefreshRateSwitcher_Exit";
        readonly EventWaitHandle _exitEvent;
        readonly RegisteredWaitHandle _exitWait;
        readonly Control _uiInvoker = new Control();

        // Test-only (--test-power <file>, used by CI): the power source and power events come from a file
        // instead of Windows, because the test machines have no battery. Never used by a normal start.
        readonly TestPower _testPower;

        public TrayApp(bool background, string testPowerFile = null)
        {
            if (testPowerFile != null)
                _testPower = new TestPower(testPowerFile, mode =>
                {
                    try { _uiInvoker.BeginInvoke((Action)(() => HandlePowerEvent(mode))); } catch { }
                });
            _settings = Settings.LoadOrDefaults();
            bool firstRun = _settings.FirstRun;
            _displayCache = new DisplayCache(() =>
            {
                var list = NativeDisplay.GetDisplays();
                // Settings exist as soon as a display is seen, so the menu and window can change them
                // straight away (not only after the next re-apply)
                bool changed = _settings.BindKeys(list);
                changed |= _settings.EnsureDisplays(list, _displayCache.Rates);
                if (changed) Save();
                return list;
            }, d => NativeDisplay.GetSupportedRates(d.DeviceName));

            _menu = new ContextMenuStrip();
            _menu.Opening += (s, e) => TrayMenu.Build(_menu, this);

            _tray = new NotifyIcon { ContextMenuStrip = _menu, Visible = true, Text = AppInfo.Name };
            _tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) ShowMenu(); };   // left-click = same menu
            _tray.BalloonTipClicked += (s, e) => OpenSettings();

            _uiInvoker.CreateControl();
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowMenuEventName);
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (st, timedOut) =>
            {
                try { _uiInvoker.BeginInvoke((Action)(() => OpenSettings())); } catch { }
            }, null, Timeout.Infinite, false);
            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (st, timedOut) =>
            {
                try { _uiInvoker.BeginInvoke((Action)ExitThread); } catch { }
            }, null, Timeout.Infinite, true);

            _debounce = new System.Windows.Forms.Timer();
            _debounce.Tick += (s, e) => { _debounce.Stop(); var r = _pendingReason; _pendingReason = ""; Apply(r); };

            // Fix an out-of-date startup entry (exe moved, or written by 1.1.x without --background).
            // Never creates one: whether to start with Windows is chosen in the installer or the app.
            try { if (Settings.StartupEntryNeedsRepair) Settings.RepairStartupEntry(); } catch { }

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            _lastAC = OnAC;
            Apply("startup");
            if (firstRun) _settings.Save();

            if (!background) OpenSettings();
            else if (firstRun)
                Notify(AppInfo.Name + " is running", "Click the tray icon to change refresh rates.");
        }

        // ------------------------------------------------------------------ IAppHost

        public Settings Settings => _settings;

        // Unknown (e.g. desktops without a battery) is treated as plugged in
        public bool OnAC => _testPower != null ? _testPower.OnAC
                          : SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Offline;

        /// <summary>Connected displays, each keyed to the settings it was saved under (shared: read-only).</summary>
        public List<DisplayInfo> Displays() => _displayCache.Displays();
        public List<int> Rates(DisplayInfo d) => _displayCache.Rates(d);

        public void SetEnabled(bool on)
        {
            _settings.Enabled = on;
            Save();
            Apply("enabled");
        }

        public void SetManaged(string key, bool on)
        {
            var ds = _settings.Get(key);
            if (ds == null) return;
            ds.Managed = on;
            Save();
            Apply("manual", key);
        }

        public void SetRate(string key, bool forAC, int hz)
        {
            var ds = _settings.Get(key);
            if (ds == null) return;
            if (forAC) ds.PluggedInHz = hz; else ds.BatteryHz = hz;
            Save();
            // Only this display is set again, and only if the rate is for the current power state:
            // a rate picked in Windows for this or another display otherwise stays
            if (forAC == OnAC) Apply("manual", key);
            else { UpdateIcon(); _form?.RefreshState(); }
        }

        public void SetNotifications(bool on)
        {
            _settings.Notifications = on;
            Save();
        }

        public bool StartupEnabled
        {
            get => Settings.StartupEnabled;
            set
            {
                try { Settings.StartupEnabled = value; }
                catch (Exception ex) { Notify("Couldn't change startup setting", ex.Message, ToolTipIcon.Warning, force: true); }
            }
        }

        public void OpenSettings(bool about = false)
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new SettingsForm(this, about);
                _form.FormClosed += (s, e) =>
                {
                    _form = null;   // a closed modeless form disposes itself
                    try { _uiInvoker.BeginInvoke((Action)MemoryTrim.AfterWindowClosed); } catch { }
                };
                _form.Show();
            }
            else
            {
                _form.ShowAbout(about);
                if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
                _form.Activate();
            }
        }

        public void Exit() => ExitThread();

        void Save()
        {
            try { _settings.Save(); }
            catch (Exception ex) { Notify("Couldn't save settings", ex.Message, ToolTipIcon.Warning, force: true); }
        }

        // ------------------------------------------------------------------ events

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (_testPower == null) HandlePowerEvent(e.Mode);
        }

        void HandlePowerEvent(PowerModes mode)
        {
            // StatusChange fires for AC/DC changes and also battery % changes: Apply() checks which.
            // Resume: some drivers reset the mode after sleep, so re-apply after a short delay.
            if (mode == PowerModes.StatusChange) Schedule("power status", 1500);
            else if (mode == PowerModes.Resume) { _displayCache.Invalidate(); Schedule("resume", 4000); }
        }

        void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            _displayCache.Invalidate();
            // Forget displays that are gone now, not only when the re-apply runs: a monitor that
            // disconnects and comes back within the wait is then set again
            try { _cache.KeepOnly(Displays().Select(d => d.Key)); } catch { }
            UpdateIcon();
            _form?.RefreshState();
            Schedule("display change", 2000);
        }

        // Re-applies everything (and forgets earlier attempts), as opposed to only touching displays
        // that changed. Apply() downgrades "power status" to a display change if the source didn't change.
        internal static bool FullApply(string reason) => reason != "display change" && reason != "retry";

        /// <summary>
        /// "power status" (any power notification) counts as a power change only if the power source
        /// actually changed. Battery-percentage updates are handled like a display change, so a rate
        /// the user picked in Windows Settings stays until they plug in or unplug.
        /// </summary>
        internal static string ResolveReason(string reason, bool ac, bool lastAC) =>
            reason != "power status" ? reason : ac != lastAC ? "power change" : "display change";

        /// <summary>Which pending re-apply wins: wake > power notification > display change / retry.</summary>
        internal static int Rank(string reason) =>
            reason == "resume" ? 3 : reason == "power status" ? 2 : reason == "" ? 0 : 1;

        void Schedule(string reason, int ms)
        {
            // Windows often sends a battery-% notification right after waking: it mustn't replace the
            // pending wake re-apply (which would then be skipped as "nothing changed")
            if (Rank(reason) < Rank(_pendingReason)) return;
            _pendingReason = reason;
            _debounce.Stop();
            _debounce.Interval = ms;
            _debounce.Start();
        }

        // ------------------------------------------------------------------ core

        /// <param name="onlyKey">A change made in the app to one display: only that display is set again.</param>
        void Apply(string reason, string onlyKey = null)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            List<DisplayInfo> displays;
            try { displays = Displays(); }
            catch { return; }

            if (_settings.EnsureDisplays(displays, Rates)) Save();

            bool ac = OnAC;
            bool sourceChanged = ac != _lastAC;
            reason = ResolveReason(reason, ac, _lastAC);
            _lastAC = ac;

            bool displayChange = !FullApply(reason) || onlyKey != null;
            if (displayChange)
            {
                _cache.KeepOnly(displays.Select(d => d.Key));   // unplugged monitors are set again when reconnected
                if (onlyKey != null) _cache.Forget(onlyKey);
            }
            else
            {
                _cache.Clear();
                _retries = 0;
            }

            var changed = new List<string>();
            bool retry = false;
            if (_settings.Enabled)
            {
                foreach (var d in displays)
                {
                    var ds = _settings.Get(d.Key);
                    if (ds == null || !ds.Managed) continue;
                    int wanted = ac ? ds.PluggedInHz : ds.BatteryHz;
                    if (wanted <= 0) { retry = true; continue; }   // driver hasn't reported rates yet

                    int target = NativeDisplay.Nearest(Rates(d), wanted);
                    if (displayChange && _cache.Skip(d.Key, d.DeviceName, target, ac)) continue;

                    string err = null;
                    if (d.CurrentHz != target)
                    {
                        err = NativeDisplay.SetRate(d.DeviceName, target);
                        _displayCache.InvalidateDisplays();   // the icon and window should show the new rate
                    }
                    if (err == null)
                    {
                        _cache.Succeeded(d.Key, d.DeviceName, target, ac);
                        if (d.CurrentHz != target)
                            changed.Add(displays.Count > 1 ? $"{d.Name}: {target} Hz" : $"Refresh rate set to {target} Hz");
                    }
                    else
                    {
                        int tries = _cache.Failed(d.Key, d.DeviceName, target, ac);
                        if (tries < ApplyCache.MaxTries) retry = true;
                        if (tries == 1 || !displayChange)   // warn once, not on every retry
                            Notify("Couldn't change refresh rate", $"{d.Name}: {target} Hz – {err}", ToolTipIcon.Warning, force: true);
                    }
                }

                // Also when the power source changed during sleep (the wake re-apply handles that)
                if (changed.Count > 0 && (reason == "power change" || reason == "manual" || reason == "enabled" ||
                                          (reason == "resume" && sourceChanged)))
                    Notify(AppInfo.StateName(ac), string.Join("\n", changed));
            }

            // Try again shortly for a change the driver refused or rates it hadn't reported yet.
            // Bounded, and never in place of a pending event (which re-applies anyway).
            if (!retry) _retries = 0;
            else if (_retries < ApplyCache.MaxTries && _pendingReason == "") { _retries++; Schedule("retry", 5000); }

            UpdateIcon();
            _form?.RefreshState();
            if (_testPower != null)
            {
                long ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                _testPower.Log($"applied {reason} in {ms} ms: " + string.Join(", ", displays.Select(d => $"{d.Key}={d.CurrentHz}")) +
                               (changed.Count > 0 ? " -> " + string.Join("; ", changed) : ""));
            }
        }

        void Notify(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, bool force = false)
        {
            if (!_settings.Notifications && !force) return;
            _testPower?.Log($"notify {icon}: {title} | {text.Replace("\n", " / ")}");
            _tray.ShowBalloonTip(3000, title, text, icon);
        }

        void ShowMenu()
        {
            var mi = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            if (mi != null) mi.Invoke(_tray, null);
            else _menu.Show(Cursor.Position);
        }

        // ------------------------------------------------------------------ icon

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

        void UpdateIcon()
        {
            List<DisplayInfo> displays;
            try { displays = Displays(); } catch { displays = new List<DisplayInfo>(); }
            var main = NativeDisplay.MainDisplay(displays);
            int hz = main?.CurrentHz ?? 0;
            bool ac = OnAC;

            Color bg = !_settings.Enabled ? Color.FromArgb(110, 110, 110)
                     : ac ? Color.FromArgb(30, 110, 220)      // blue = plugged in
                          : Color.FromArgb(220, 130, 20);     // amber = battery

            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            var look = (hz, bg.ToArgb(), size);
            if (_currentIcon == null || look != _iconDrawn)   // redraw only when what it shows changes
            {
                _iconDrawn = look;
                DrawIcon(hz, bg, size);
            }

            string second;
            if (!_settings.Enabled) second = "Auto-switch off";
            else if (displays.Count > 1)
                second = $"Auto-switch on · {displays.Count(d => _settings.Get(d.Key)?.Managed == true)} of {displays.Count} displays";
            else
            {
                var ds = main != null ? _settings.Get(main.Key) : null;
                second = ds == null || !ds.Managed ? "Display not switched"
                       : $"Auto: {ds.PluggedInHz} Hz AC / {ds.BatteryHz} Hz battery";
            }
            string tip = $"{hz} Hz · {AppInfo.StateName(ac)}\n{second}";
            _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;   // .NET Framework limit
        }

        void DrawIcon(int hz, Color bg, int size)
        {
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(bg))
                using (var path = RoundRect(new RectangleF(0, 0, size - 1, size - 1), size * 0.22f))
                    g.FillPath(b, path);

                string text = hz > 0 ? hz.ToString() : "?";
                float fontPx = size * (text.Length >= 3 ? 0.56f : 0.72f);
                using (var f = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    var sz = g.MeasureString(text, f, PointF.Empty, StringFormat.GenericTypographic);
                    float scale = Math.Min(1f, (size * 0.96f) / sz.Width);   // shrink until it fits
                    using (var f2 = new Font("Segoe UI", fontPx * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(text, f2, Brushes.White, new RectangleF(0, 0, size, size + 1), sf);
                }

                IntPtr h = bmp.GetHicon();
                var newIcon = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                _tray.Icon = newIcon;
                _currentIcon?.Dispose();
                _currentIcon = newIcon;
            }
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ------------------------------------------------------------------ shutdown

        protected override void ExitThreadCore()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _form?.Close();
            _debounce.Dispose();
            _showWait.Unregister(null);
            _showEvent.Dispose();
            _exitWait.Unregister(null);
            _exitEvent.Dispose();
            _testPower?.Dispose();
            _uiInvoker.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();
            (_menu.Tag as Font)?.Dispose();
            _currentIcon?.Dispose();
            base.ExitThreadCore();
        }
    }

    /// <summary>
    /// What Apply() has done per display for the current power state. A display-change event
    /// (monitor plugged in, resolution changed, or our own change) skips displays already set, so a
    /// rate the user picks in Windows Settings isn't fought over and a driver that reports a slightly
    /// different rate can't cause a loop. Only a change that worked counts as set: one the driver
    /// refused (e.g. mid-dock) is tried again on later events, up to MaxTries times, so a driver that
    /// keeps refusing can't cause a stream of warnings. A power change starts afresh.
    /// Each entry also remembers the display's Windows device name (\\.\DISPLAYn): a monitor that
    /// disconnects and reconnects gets a new one, and comes back at its default rate, so it's set again.
    /// </summary>
    internal sealed class ApplyCache
    {
        public const int MaxTries = 3;
        readonly Dictionary<string, (string dev, int hz, bool ac)> _applied = new Dictionary<string, (string, int, bool)>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, (string dev, int hz, bool ac, int tries)> _failed = new Dictionary<string, (string, int, bool, int)>(StringComparer.OrdinalIgnoreCase);

        static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

        /// <summary>True if a display-change event should leave this display alone.</summary>
        public bool Skip(string key, string dev, int hz, bool ac) =>
            (_applied.TryGetValue(key, out var a) && Same(a.dev, dev) && a.hz == hz && a.ac == ac) ||
            (_failed.TryGetValue(key, out var f) && Same(f.dev, dev) && f.hz == hz && f.ac == ac && f.tries >= MaxTries);

        public void Succeeded(string key, string dev, int hz, bool ac)
        {
            _applied[key] = (dev, hz, ac);
            _failed.Remove(key);
        }

        /// <summary>Records a refused change; returns how many times in a row this target has failed.</summary>
        public int Failed(string key, string dev, int hz, bool ac)
        {
            int tries = _failed.TryGetValue(key, out var f) && Same(f.dev, dev) && f.hz == hz && f.ac == ac ? f.tries + 1 : 1;
            _failed[key] = (dev, hz, ac, tries);
            _applied.Remove(key);
            return tries;
        }

        /// <summary>The user changed this display's settings in the app: set it again.</summary>
        public void Forget(string key)
        {
            _applied.Remove(key);
            _failed.Remove(key);
        }

        public void KeepOnly(IEnumerable<string> connectedKeys)
        {
            var keep = new HashSet<string>(connectedKeys, StringComparer.OrdinalIgnoreCase);
            foreach (var k in _applied.Keys.Where(k => !keep.Contains(k)).ToList()) _applied.Remove(k);
            foreach (var k in _failed.Keys.Where(k => !keep.Contains(k)).ToList()) _failed.Remove(k);
        }

        public void Clear()
        {
            _applied.Clear();
            _failed.Clear();
        }
    }

    /// <summary>
    /// Test-only power source for CI machines, which have no battery. The file holds one line:
    /// "ac" or "battery", optionally followed by "resume" (woke from sleep) or "tick" (battery-%
    /// update, same source) and a number to make each write unique, e.g. "battery tick 3".
    /// Each time the file is rewritten the app reacts as it would to the real
    /// Windows event. What the app does is written to a .log file next to it.
    /// </summary>
    internal sealed class TestPower : IDisposable
    {
        readonly string _file, _log;
        readonly FileSystemWatcher _watch;
        readonly Action<PowerModes> _raise;
        readonly object _lock = new object();
        string _last = "";
        public bool OnAC { get; private set; } = true;

        public TestPower(string file, Action<PowerModes> raise)
        {
            _file = Path.GetFullPath(file);
            _log = Path.ChangeExtension(_file, ".log");
            _raise = raise;
            if (!File.Exists(_file)) File.WriteAllText(_file, "ac");
            Read(out _);
            Log("test power source: " + (OnAC ? "ac" : "battery"));
            _watch = new FileSystemWatcher(Path.GetDirectoryName(_file), Path.GetFileName(_file))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _watch.Changed += (s, e) => Changed();
            _watch.Created += (s, e) => Changed();
            _watch.Renamed += (s, e) => Changed();
            _watch.EnableRaisingEvents = true;
        }

        bool Read(out string text)
        {
            text = null;
            for (int i = 0; i < 10; i++)
            {
                try { text = File.ReadAllText(_file).Trim().ToLowerInvariant(); break; }
                catch (IOException) { Thread.Sleep(50); }
            }
            if (string.IsNullOrEmpty(text)) return false;
            OnAC = !text.StartsWith("battery");
            return true;
        }

        void Changed()
        {
            lock (_lock)
            {
                if (!Read(out var text) || text == _last) return;   // one write can raise several events
                _last = text;
                Log("power event: " + text);
                _raise(text.Contains("resume") ? PowerModes.Resume : PowerModes.StatusChange);
            }
        }

        public void Log(string line)
        {
            try { lock (_lock) File.AppendAllText(_log, $"{DateTime.Now:HH:mm:ss.fff} {line}\r\n"); } catch { }
        }

        public void Dispose() => _watch.Dispose();
    }

    internal static class MemoryTrim
    {
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

        /// <summary>
        /// The settings window uses a few MB while open. After it closes, free that memory
        /// so the app returns to its small tray-only footprint.
        /// </summary>
        public static void AfterWindowClosed()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                using (var p = System.Diagnostics.Process.GetCurrentProcess())
                    SetProcessWorkingSetSize(p.Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }
    }
}
