// Refresh Rate Switcher
// Copyright (C) 2026 Noel Chan
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
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

        // Rate last applied to each display for the current power state. A display-change event
        // (monitor plugged in, resolution changed, or our own change) only touches displays not in
        // here, so a rate the user picks in Windows Settings isn't fought over, and a driver that
        // reports a slightly different rate can't cause a loop.
        readonly Dictionary<string, (int hz, bool ac)> _applied = new Dictionary<string, (int, bool)>(StringComparer.OrdinalIgnoreCase);

        // Signalled by a second launch (e.g. Start Menu shortcut) to open this instance's window
        public const string ShowMenuEventName = @"Local\RefreshRateSwitcher_ShowMenu";
        readonly EventWaitHandle _showEvent;
        readonly RegisteredWaitHandle _showWait;
        readonly Control _uiInvoker = new Control();

        public TrayApp(bool background)
        {
            _settings = Settings.Load();
            bool firstRun = _settings.FirstRun;

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

            _debounce = new System.Windows.Forms.Timer();
            _debounce.Tick += (s, e) => { _debounce.Stop(); var r = _pendingReason; _pendingReason = ""; Apply(r); };

            // Fix an out-of-date startup entry (exe moved, or written by 1.1.x without --background).
            // Never creates one: whether to start with Windows is chosen in the installer or the app.
            try { if (Settings.StartupEntryNeedsRepair) Settings.StartupEnabled = true; } catch { }

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            Apply("startup");
            if (firstRun) _settings.Save();

            if (!background) OpenSettings();
            else if (firstRun)
                Notify(AppInfo.Name + " is running", "Click the tray icon to change refresh rates.");
        }

        // ------------------------------------------------------------------ IAppHost

        public Settings Settings => _settings;

        // Unknown (e.g. desktops without a battery) is treated as plugged in
        public bool OnAC => SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Offline;

        public List<DisplayInfo> Displays() => NativeDisplay.GetDisplays();
        public List<int> Rates(DisplayInfo d) => NativeDisplay.GetSupportedRates(d.DeviceName);

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
            Apply("manual");
        }

        public void SetRate(string key, bool forAC, int hz)
        {
            var ds = _settings.Get(key);
            if (ds == null) return;
            if (forAC) ds.PluggedInHz = hz; else ds.BatteryHz = hz;
            Save();
            Apply("manual");
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
            // StatusChange fires for AC/DC changes (and battery % changes – Apply() is a no-op then).
            // Resume: some drivers reset the mode after sleep, so re-apply after a short delay.
            if (e.Mode == PowerModes.StatusChange) Schedule("power change", 1500);
            else if (e.Mode == PowerModes.Resume) Schedule("resume", 4000);
        }

        void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            UpdateIcon();
            _form?.RefreshState();
            Schedule("display change", 2000);
        }

        void Schedule(string reason, int ms)
        {
            // A pending power/resume re-apply outranks a display-change one
            if (reason == "display change" && _pendingReason != "" && _pendingReason != "display change") return;
            _pendingReason = reason;
            _debounce.Stop();
            _debounce.Interval = ms;
            _debounce.Start();
        }

        // ------------------------------------------------------------------ core

        void Apply(string reason)
        {
            List<DisplayInfo> displays;
            try { displays = NativeDisplay.GetDisplays(); }
            catch { return; }

            if (_settings.EnsureDisplays(displays, Rates)) Save();

            bool displayChange = reason == "display change";
            if (displayChange)
            {
                // Forget unplugged monitors so they're set again when reconnected
                var connected = new HashSet<string>(displays.Select(d => d.Key), StringComparer.OrdinalIgnoreCase);
                foreach (var k in _applied.Keys.Where(k => !connected.Contains(k)).ToList()) _applied.Remove(k);
            }
            else
                _applied.Clear();

            var changed = new List<string>();
            if (_settings.Enabled)
            {
                bool ac = OnAC;
                foreach (var d in displays)
                {
                    var ds = _settings.Get(d.Key);
                    if (ds == null || !ds.Managed) continue;
                    int wanted = ac ? ds.PluggedInHz : ds.BatteryHz;
                    if (wanted <= 0) continue;

                    int target = NativeDisplay.Nearest(Rates(d), wanted);
                    if (displayChange && _applied.TryGetValue(d.Key, out var last) && last.hz == target && last.ac == ac) continue;
                    _applied[d.Key] = (target, ac);

                    if (d.CurrentHz == target) continue;
                    string err = NativeDisplay.SetRate(d.DeviceName, target);
                    if (err != null)
                        Notify("Couldn't change refresh rate", $"{d.Name}: {target} Hz – {err}", ToolTipIcon.Warning, force: true);
                    else
                        changed.Add(displays.Count > 1 ? $"{d.Name}: {target} Hz" : $"Refresh rate set to {target} Hz");
                }

                if (changed.Count > 0 && (reason == "power change" || reason == "manual" || reason == "enabled"))
                    Notify(AppInfo.StateName(ac), string.Join("\n", changed));
            }

            UpdateIcon();
            _form?.RefreshState();
        }

        void Notify(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, bool force = false)
        {
            if (!_settings.Notifications && !force) return;
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
            try { displays = NativeDisplay.GetDisplays(); } catch { displays = new List<DisplayInfo>(); }
            var main = NativeDisplay.MainDisplay(displays);
            int hz = main?.CurrentHz ?? 0;
            bool ac = OnAC;

            Color bg = !_settings.Enabled ? Color.FromArgb(110, 110, 110)
                     : ac ? Color.FromArgb(30, 110, 220)      // blue = plugged in
                          : Color.FromArgb(220, 130, 20);     // amber = battery

            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
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
            _uiInvoker.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();
            (_menu.Tag as Font)?.Dispose();
            _currentIcon?.Dispose();
            base.ExitThreadCore();
        }
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
