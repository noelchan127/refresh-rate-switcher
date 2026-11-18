// Refresh Rate Switcher
// Copyright (C) 2026 noelchan127
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
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
    internal sealed class TrayApp : ApplicationContext
    {
        readonly NotifyIcon _tray;
        readonly ContextMenuStrip _menu;
        readonly Settings _settings;
        readonly System.Windows.Forms.Timer _debounce;
        string _pendingReason = "";
        Icon _currentIcon;

        public const string ShowMenuEventName = @"Local\RefreshRateSwitcher_ShowMenu";
        readonly EventWaitHandle _showMenuEvent;
        readonly RegisteredWaitHandle _showMenuWait;
        readonly Control _uiInvoker = new Control();

        public TrayApp()
        {
            _settings = Settings.Load();

            _menu = new ContextMenuStrip();
            _menu.Opening += (s, e) => BuildMenu();

            _tray = new NotifyIcon { ContextMenuStrip = _menu, Visible = true, Text = "Refresh Rate Switcher" };
            _tray.MouseUp += (s, e) =>
            {
                // Left-click opens the same menu as right-click
                if (e.Button == MouseButtons.Left) ShowMenu();
            };

            // Lets a second launch (Start Menu shortcut) open this instance's menu
            _uiInvoker.CreateControl();
            _showMenuEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowMenuEventName);
            _showMenuWait = ThreadPool.RegisterWaitForSingleObject(_showMenuEvent, (st, timedOut) =>
            {
                try { _uiInvoker.BeginInvoke((Action)ShowMenu); } catch { }
            }, null, Timeout.Infinite, false);

            _debounce = new System.Windows.Forms.Timer();
            _debounce.Tick += (s, e) => { _debounce.Stop(); Apply(_pendingReason); };

            if (_settings.FirstRun) InitDefaults();
            // If the exe was moved, repoint the existing startup entry at the new location
            try { if (Settings.StartupEntryStale) Settings.StartupEnabled = true; } catch { }

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            Apply("startup");
            UpdateIcon();
        }

        // ------------------------------------------------------------------ state

        static bool OnAC
        {
            get
            {
                // Unknown (e.g. desktops without a battery) is treated as plugged in
                return SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Offline;
            }
        }

        static string StateName(bool ac) => ac ? "Plugged in" : "On battery";

        void InitDefaults()
        {
            var d = NativeDisplay.Resolve(_settings.Display);
            if (d != null)
            {
                var rates = NativeDisplay.GetSupportedRates(d.DeviceName);
                if (rates.Count > 0)
                {
                    _settings.PluggedInHz = rates.Max();
                    _settings.BatteryHz = rates.Contains(60) ? 60 : rates.Min();
                }
            }
            _settings.Save();
            try { Settings.StartupEnabled = true; } catch { }
            Notify("Refresh Rate Switcher is running",
                   $"Plugged in: {_settings.PluggedInHz} Hz, on battery: {_settings.BatteryHz} Hz.\nClick the tray icon to change.");
        }

        // ------------------------------------------------------------------ events

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            // StatusChange fires for AC/DC changes (and battery % changes – Apply() is a no-op then).
            // Resume: some drivers reset the mode after sleep, so re-apply after a short delay.
            if (e.Mode == PowerModes.StatusChange) Schedule("power change", 1500);
            else if (e.Mode == PowerModes.Resume) Schedule("resume", 4000);
        }

        void OnDisplaySettingsChanged(object sender, EventArgs e) => UpdateIcon();

        void Schedule(string reason, int ms)
        {
            _pendingReason = reason;
            _debounce.Stop();
            _debounce.Interval = ms;
            _debounce.Start();
        }

        // ------------------------------------------------------------------ core

        void Apply(string reason)
        {
            if (!_settings.Enabled) { UpdateIcon(); return; }

            var display = NativeDisplay.Resolve(_settings.Display);
            if (display == null) return;

            bool ac = OnAC;
            int wanted = ac ? _settings.PluggedInHz : _settings.BatteryHz;
            if (wanted <= 0) { UpdateIcon(); return; }

            var rates = NativeDisplay.GetSupportedRates(display.DeviceName);
            int target = NativeDisplay.Nearest(rates, wanted);
            int before = NativeDisplay.GetCurrentRate(display.DeviceName);

            if (before != target)
            {
                string err = NativeDisplay.SetRate(display.DeviceName, target);
                if (err != null)
                    Notify("Couldn't change refresh rate", $"{target} Hz on {display.DeviceName.Replace(@"\\.\", "")}: {err}", ToolTipIcon.Warning, force: true);
                else if (reason != "startup" && reason != "resume")
                    Notify(StateName(ac), $"Refresh rate set to {target} Hz");
            }
            UpdateIcon();
        }

        void Notify(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, bool force = false)
        {
            if (!_settings.Notifications && !force) return;
            _tray.ShowBalloonTip(3000, title, text, icon);
        }

        // ------------------------------------------------------------------ menu

        void BuildMenu()
        {
            _menu.Items.Clear();
            var display = NativeDisplay.Resolve(_settings.Display);
            var rates = display != null ? NativeDisplay.GetSupportedRates(display.DeviceName) : new System.Collections.Generic.List<int>();
            int current = display != null ? NativeDisplay.GetCurrentRate(display.DeviceName) : 0;
            bool ac = OnAC;

            var header = new ToolStripMenuItem($"{StateName(ac)} · now {current} Hz") { Enabled = false };
            _menu.Items.Add(header);
            _menu.Items.Add(new ToolStripSeparator());

            var enabled = new ToolStripMenuItem("Auto-switch enabled") { Checked = _settings.Enabled, CheckOnClick = false };
            enabled.Click += (s, e) =>
            {
                _settings.Enabled = !_settings.Enabled;
                _settings.Save();
                if (_settings.Enabled) Apply("enabled"); else UpdateIcon();
            };
            _menu.Items.Add(enabled);

            _menu.Items.Add(RateMenu("Plugged in", true, rates, current, ac));
            _menu.Items.Add(RateMenu("On battery", false, rates, current, ac));

            _menu.Items.Add(new ToolStripSeparator());

            // Display selection
            var dispMenu = new ToolStripMenuItem("Display");
            var displays = NativeDisplay.GetDisplays();
            var autoTarget = NativeDisplay.Resolve("auto");
            var autoItem = new ToolStripMenuItem("Automatic" + (autoTarget != null ? $" ({autoTarget.DeviceName.Replace(@"\\.\", "")})" : ""))
            { Checked = _settings.Display == "auto" };
            autoItem.Click += (s, e) => { _settings.Display = "auto"; _settings.Save(); Apply("display"); };
            dispMenu.DropDownItems.Add(autoItem);
            dispMenu.DropDownItems.Add(new ToolStripSeparator());
            foreach (var d in displays)
            {
                var name = d.DeviceName;
                var it = new ToolStripMenuItem(d.Label) { Checked = _settings.Display == name };
                it.Click += (s, e) => { _settings.Display = name; _settings.Save(); Apply("display"); };
                dispMenu.DropDownItems.Add(it);
            }
            _menu.Items.Add(dispMenu);

            bool startup = false;
            try { startup = Settings.StartupEnabled; } catch { }
            var startItem = new ToolStripMenuItem("Start with Windows") { Checked = startup };
            startItem.Click += (s, e) =>
            {
                try { Settings.StartupEnabled = !startup; }
                catch (Exception ex) { Notify("Couldn't change startup setting", ex.Message, ToolTipIcon.Warning, true); }
            };
            _menu.Items.Add(startItem);

            var notifItem = new ToolStripMenuItem("Show notifications") { Checked = _settings.Notifications };
            notifItem.Click += (s, e) => { _settings.Notifications = !_settings.Notifications; _settings.Save(); };
            _menu.Items.Add(notifItem);

            _menu.Items.Add(new ToolStripSeparator());
            var exit = new ToolStripMenuItem("Exit");
            exit.Click += (s, e) => ExitThread();
            _menu.Items.Add(exit);
        }

        ToolStripMenuItem RateMenu(string title, bool forAC, System.Collections.Generic.List<int> rates, int current, bool onAC)
        {
            int chosen = forAC ? _settings.PluggedInHz : _settings.BatteryHz;
            int effective = NativeDisplay.Nearest(rates, chosen);
            var m = new ToolStripMenuItem($"{title}: {(chosen > 0 ? effective + " Hz" : "not set")}");
            if (forAC == onAC) m.Font = new Font(m.Font, FontStyle.Bold);   // highlight the active state

            if (rates.Count == 0)
                m.DropDownItems.Add(new ToolStripMenuItem("No rates reported by driver") { Enabled = false });

            foreach (int hz in rates.OrderByDescending(r => r))
            {
                int value = hz;
                var it = new ToolStripMenuItem($"{hz} Hz" + (hz == current ? "   (current)" : "")) { Checked = hz == effective && chosen > 0 };
                it.Click += (s, e) =>
                {
                    if (forAC) _settings.PluggedInHz = value; else _settings.BatteryHz = value;
                    _settings.Save();
                    if (forAC == OnAC) Apply("manual");
                };
                m.DropDownItems.Add(it);
            }
            return m;
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
            var display = NativeDisplay.Resolve(_settings.Display);
            int hz = display != null ? NativeDisplay.GetCurrentRate(display.DeviceName) : 0;
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
                    // shrink until it fits
                    var sz = g.MeasureString(text, f, PointF.Empty, StringFormat.GenericTypographic);
                    float scale = Math.Min(1f, (size * 0.96f) / sz.Width);
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

            string tip = $"{hz} Hz · {StateName(ac)}\n" +
                         (_settings.Enabled ? $"Auto: {_settings.PluggedInHz} Hz AC / {_settings.BatteryHz} Hz battery" : "Auto-switch disabled");
            _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;   // NotifyIcon limit
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
            _debounce.Dispose();
            _showMenuWait.Unregister(null);
            _showMenuEvent.Dispose();
            _uiInvoker.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _currentIcon?.Dispose();
            base.ExitThreadCore();
        }
    }
}
