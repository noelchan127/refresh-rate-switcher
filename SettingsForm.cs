// Refresh Rate Switcher
// Copyright (C) 2026 Noel Chan
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RefreshRateSwitcher
{
    /// <summary>
    /// The settings window. Created only when opened and disposed when closed, so it costs nothing
    /// while the app sits in the tray. Every change applies immediately (no Save button).
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        readonly IAppHost _host;
        readonly float _sim;                 // >1 only in --selftest, to preview high-DPI layout
        readonly TabControl _tabs;
        readonly TabPage _settingsPage, _aboutPage;
        readonly TableLayoutPanel _settingsBody, _aboutBody, _displaysPanel;
        readonly Label _status;
        readonly CheckBox _chkEnabled, _chkStartup, _chkNotify;
        readonly List<Label> _wrapLabels = new List<Label>();
        readonly Dictionary<string, DisplayRow> _rows = new Dictionary<string, DisplayRow>(StringComparer.OrdinalIgnoreCase);
        readonly Font _boldFont, _titleFont;
        readonly Font _baseFont;
        Icon _icon;
        bool _loading;
        bool _constructed;   // after the constructor, new controls must be scaled by hand

        sealed class DisplayRow
        {
            public GroupBox Box;
            public CheckBox Managed;
            public ComboBox AC, Battery;
            public Label Info;
        }

        sealed class RateItem
        {
            public int Hz;
            public override string ToString() => Hz + " Hz";
        }

        public SettingsForm(IAppHost host, bool about = false, float simulateScale = 1f)
        {
            _host = host;
            _sim = simulateScale;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f / _sim, 96f / _sim);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = _baseFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 9f * _sim);
            _boldFont = new Font(Font, FontStyle.Bold);
            _titleFont = new Font(Font.FontFamily, 13f * _sim, FontStyle.Bold);

            Text = AppInfo.Name;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ShowInTaskbar = true;
            try { _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); Icon = _icon; } catch { }

            // ---------- Settings tab ----------
            _settingsBody = Column();
            _status = new Label { AutoSize = true, Font = _boldFont, Margin = new Padding(3, 0, 3, 6) };
            _settingsBody.Controls.Add(_status);

            _chkEnabled = new CheckBox { Text = "Switch refresh rates automatically", AutoSize = true, Margin = new Padding(3, 0, 3, 6) };
            _chkEnabled.CheckedChanged += (s, e) => { if (!_loading) _host.SetEnabled(_chkEnabled.Checked); };
            _settingsBody.Controls.Add(_chkEnabled);

            _displaysPanel = Column();
            _displaysPanel.Padding = Padding.Empty;
            _displaysPanel.Margin = Padding.Empty;
            _settingsBody.Controls.Add(_displaysPanel);

            var general = Group("General");
            var gTable = Column();
            gTable.Padding = new Padding(4, 2, 4, 2);
            gTable.Dock = DockStyle.Fill;
            _chkStartup = new CheckBox { Text = "Start with Windows", AutoSize = true };
            _chkStartup.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                try { _host.StartupEnabled = _chkStartup.Checked; }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Couldn't change the startup setting:\n" + ex.Message, AppInfo.Name,
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                RefreshState();
            };
            _chkNotify = new CheckBox { Text = "Show notifications when the rate changes", AutoSize = true };
            _chkNotify.CheckedChanged += (s, e) => { if (!_loading) _host.SetNotifications(_chkNotify.Checked); };
            gTable.Controls.Add(_chkStartup);
            gTable.Controls.Add(_chkNotify);
            general.Controls.Add(gTable);
            _settingsBody.Controls.Add(general);

            _settingsBody.Controls.Add(Wrap("Closing this window keeps Refresh Rate Switcher running in the system tray. " +
                                            "To quit, right-click the tray icon and choose Exit.", grey: true));

            // ---------- About tab ----------
            _aboutBody = Column();
            _aboutBody.Controls.Add(new Label { Text = AppInfo.Name, AutoSize = true, Font = _titleFont, Margin = new Padding(3, 0, 3, 0) });
            _aboutBody.Controls.Add(new Label { Text = "Version " + AppInfo.Version, AutoSize = true, Margin = new Padding(3, 2, 3, 10) });
            _aboutBody.Controls.Add(Link("Made by ", AppInfo.Author, "", AppInfo.AuthorUrl));
            _aboutBody.Controls.Add(Link("Licence: ", AppInfo.License, "", AppInfo.LicenseUrl));
            _aboutBody.Controls.Add(Wrap("Free, open-source software provided without any warranty. " +
                                         "You may share and modify it under the licence terms.", grey: true));
            _aboutBody.Controls.Add(Link("Updates: ", "download the latest version", "", AppInfo.ReleasesUrl, top: 10));
            _aboutBody.Controls.Add(Link("Source code and bug reports: ", "GitHub", "", AppInfo.RepoUrl));
            _aboutBody.Controls.Add(Wrap("The app never connects to the internet. Links open in your browser, " +
                                         "and updates are installed by running the new installer.", grey: true));
            var copy = new Button { Text = "Copy system info", AutoSize = true, Margin = new Padding(3, 10, 3, 0) };
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(DiagnosticText(_host)); copy.Text = "Copied ✓"; }
                catch { copy.Text = "Couldn't copy"; }
            };
            _aboutBody.Controls.Add(copy);
            _aboutBody.Controls.Add(Wrap("Paste this into a bug report so I can see your display setup.", grey: true));

            // ---------- Window ----------
            _settingsPage = new TabPage("Settings") { UseVisualStyleBackColor = true, Padding = Padding.Empty, AutoScroll = true };
            _aboutPage = new TabPage("About") { UseVisualStyleBackColor = true, Padding = Padding.Empty, AutoScroll = true };
            _settingsPage.Controls.Add(_settingsBody);
            _aboutPage.Controls.Add(_aboutBody);
            _tabs = new TabControl { Margin = new Padding(8, 8, 8, 0) };
            _tabs.TabPages.Add(_settingsPage);
            _tabs.TabPages.Add(_aboutPage);

            var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(80, 0), Margin = new Padding(3, 8, 8, 8) };
            close.Click += (s, e) => Close();
            CancelButton = close;

            var bottom = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
            bottom.Controls.Add(close);

            var root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty, Location = Point.Empty };
            root.Controls.Add(_tabs, 0, 0);
            root.Controls.Add(bottom, 0, 1);
            Controls.Add(root);

            RebuildDisplays();
            LoadState();
            ResumeLayout(true);

            _constructed = true;
            FitTabs();
            if (about) _tabs.SelectedTab = _aboutPage;
        }

        public void ShowAbout(bool about) => _tabs.SelectedTab = about ? _aboutPage : _settingsPage;

        // ------------------------------------------------------------------ building blocks

        static TableLayoutPanel Column() => new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(10, 10, 10, 6),
            Location = Point.Empty,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };

        static GroupBox Group(string text) => new GroupBox
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 4, 6, 6),
            Margin = new Padding(3, 3, 3, 8),
        };

        Label Wrap(string text, bool grey = false)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Margin = new Padding(3, 2, 3, 2),
                ForeColor = grey ? SystemColors.GrayText : SystemColors.ControlText,
            };
            _wrapLabels.Add(l);
            return l;
        }

        static LinkLabel Link(string before, string linkText, string after, string url, int top = 2)
        {
            var l = new LinkLabel { Text = before + linkText + after, AutoSize = true, Margin = new Padding(3, top, 3, 2) };
            l.Links.Clear();
            l.Links.Add(before.Length, linkText.Length, url);
            l.LinkClicked += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo((string)e.Link.LinkData) { UseShellExecute = true }); } catch { }
            };
            return l;
        }

        // Sizes here are at 100% scaling; WinForms scales them for the monitor's DPI
        static ComboBox RateCombo() =>
            new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 84, Margin = new Padding(3, 3, 12, 3) };

        // ------------------------------------------------------------------ displays

        void RebuildDisplays()
        {
            _displaysPanel.SuspendLayout();
            var oldControls = _displaysPanel.Controls.Cast<Control>().ToList();
            _displaysPanel.Controls.Clear();
            foreach (var c in oldControls) c.Dispose();
            _rows.Clear();

            var displays = _host.Displays();
            if (displays.Count == 0)
                _displaysPanel.Controls.Add(new Label { Text = "No displays found.", AutoSize = true });

            foreach (var d in displays)
            {
                var row = new DisplayRow { Box = Group(d.Name + (d.IsPrimary && displays.Count > 1 ? "  (primary)" : "")) };
                var t = new TableLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 4,
                    Dock = DockStyle.Fill,
                    Padding = new Padding(4, 2, 4, 0),
                };
                string key = d.Key;

                row.Managed = new CheckBox { Text = "Switch this display automatically", AutoSize = true };
                row.Managed.CheckedChanged += (s, e) => { if (!_loading) _host.SetManaged(key, row.Managed.Checked); };
                t.Controls.Add(row.Managed, 0, 0);
                t.SetColumnSpan(row.Managed, 4);

                row.AC = RateCombo();
                row.Battery = RateCombo();
                var rates = _host.Rates(d);
                foreach (var hz in rates.OrderByDescending(r => r))
                {
                    row.AC.Items.Add(new RateItem { Hz = hz });
                    row.Battery.Items.Add(new RateItem { Hz = hz });
                }
                row.AC.SelectedIndexChanged += (s, e) => { if (!_loading && row.AC.SelectedItem is RateItem r) _host.SetRate(key, true, r.Hz); };
                row.Battery.SelectedIndexChanged += (s, e) => { if (!_loading && row.Battery.SelectedItem is RateItem r) _host.SetRate(key, false, r.Hz); };

                t.Controls.Add(new Label { Text = "Plugged in:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
                t.Controls.Add(row.AC, 1, 1);
                t.Controls.Add(new Label { Text = "On battery:", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 1);
                t.Controls.Add(row.Battery, 3, 1);

                row.Info = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 4, 3, 0) };
                t.Controls.Add(row.Info, 0, 2);
                t.SetColumnSpan(row.Info, 4);

                row.Box.Controls.Add(t);
                if (_constructed) { float f = Scale100(); row.Box.Scale(new SizeF(f, f)); }
                _displaysPanel.Controls.Add(row.Box);
                _rows[key] = row;
            }
            _displaysPanel.ResumeLayout(true);
        }

        void LoadState()
        {
            _loading = true;
            try
            {
                var s = _host.Settings;
                bool ac = _host.OnAC;
                _status.Text = "Power: " + AppInfo.StateName(ac);
                _chkEnabled.Checked = s.Enabled;
                _chkNotify.Checked = s.Notifications;
                bool startup = false;
                try { startup = _host.StartupEnabled; } catch { }
                _chkStartup.Checked = startup;

                foreach (var d in _host.Displays())
                {
                    if (!_rows.TryGetValue(d.Key, out var row)) continue;
                    var ds = s.Get(d.Key);
                    bool managed = ds != null && ds.Managed;
                    row.Managed.Checked = managed;
                    bool hasRates = row.AC.Items.Count > 0;
                    row.AC.Enabled = row.Battery.Enabled = managed && hasRates && s.Enabled;
                    if (!row.AC.DroppedDown) Select(row.AC, ds?.PluggedInHz ?? 0);
                    if (!row.Battery.DroppedDown) Select(row.Battery, ds?.BatteryHz ?? 0);

                    string state = !hasRates ? "no refresh rates reported by the driver"
                                 : !managed ? "not switched"
                                 : !s.Enabled ? "auto-switch is off"
                                 : "using the " + (ac ? "plugged-in" : "battery") + " rate";
                    row.Info.Text = $"Now {d.CurrentHz} Hz at {d.Width} × {d.Height} · {state}";
                }
            }
            finally { _loading = false; }
        }

        static void Select(ComboBox c, int hz)
        {
            var items = c.Items.Cast<RateItem>().ToList();
            if (items.Count == 0 || hz <= 0) { c.SelectedIndex = -1; return; }
            int nearest = NativeDisplay.Nearest(items.Select(i => i.Hz).ToList(), hz);
            c.SelectedIndex = items.FindIndex(i => i.Hz == nearest);
        }

        /// <summary>Called by the app after power, display or setting changes.</summary>
        public void RefreshState()
        {
            if (IsDisposed) return;
            var keys = _host.Displays().Select(d => d.Key).ToList();
            bool sameDisplays = keys.Count == _rows.Count && keys.All(_rows.ContainsKey);
            if (!sameDisplays)
            {
                SuspendLayout();
                RebuildDisplays();
                ResumeLayout(true);
            }
            LoadState();
            if (!sameDisplays) FitTabs();
        }

        // TabControl can't auto-size, so size it to the larger of the two pages
        void FitTabs()
        {
            var tabsHandle = _tabs.Handle;   // DisplayRectangle needs a real window
            var sp = _settingsBody.GetPreferredSize(Size.Empty);
            int wrap = Math.Max(sp.Width - _settingsBody.Padding.Horizontal - 8, (int)(320 * Scale100()));
            foreach (var l in _wrapLabels) l.MaximumSize = new Size(wrap, 0);
            sp = _settingsBody.GetPreferredSize(Size.Empty);
            var ap = _aboutBody.GetPreferredSize(Size.Empty);
            var content = new Size(Math.Max(sp.Width, ap.Width), Math.Max(sp.Height, ap.Height));
            var chrome = new Size(_tabs.Width - _tabs.DisplayRectangle.Width, _tabs.Height - _tabs.DisplayRectangle.Height);

            // With many displays or high scaling the window could be taller than the screen: scroll instead
            int around = Height - _tabs.Height;   // title bar, borders, Close button
            int maxContent = Screen.FromControl(this).WorkingArea.Height - around - chrome.Height - (int)(16 * Scale100());
            if (content.Height > maxContent && maxContent > 100)
            {
                content.Height = maxContent;
                content.Width += SystemInformation.VerticalScrollBarWidth;
            }
            _tabs.Size = new Size(content.Width + chrome.Width, content.Height + chrome.Height);
        }

        float Scale100() => DeviceDpi / 96f * _sim;

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            FitTabs();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _boldFont.Dispose();
                _titleFont.Dispose();
                _baseFont.Dispose();
                _icon?.Dispose();
            }
        }

        // ------------------------------------------------------------------ diagnostics

        internal static string DiagnosticText(IAppHost host)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{AppInfo.Name} {AppInfo.Version}");
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    sb.AppendLine($"Windows: {k?.GetValue("ProductName")} {k?.GetValue("DisplayVersion")} build {k?.GetValue("CurrentBuild")}.{k?.GetValue("UBR")}");
            }
            catch { sb.AppendLine("Windows: " + Environment.OSVersion); }
            var ps = SystemInformation.PowerStatus;
            sb.AppendLine($"Power: {ps.PowerLineStatus}, battery {ps.BatteryChargeStatus}");
            sb.AppendLine($"Auto-switch: {(host.Settings.Enabled ? "on" : "off")}");
            foreach (var d in host.Displays())
            {
                var ds = host.Settings.Get(d.Key);
                sb.AppendLine($"- {d.Name} [{d.ShortDeviceName}{(d.IsInternal ? ", built-in" : "")}{(d.IsPrimary ? ", primary" : "")}] " +
                              $"{d.Width}x{d.Height} @ {d.CurrentHz} Hz, tech {d.OutputTech}, id {d.Key}");
                sb.AppendLine($"  rates: {string.Join(", ", host.Rates(d))}");
                sb.AppendLine(ds == null ? "  settings: none"
                    : $"  settings: {(ds.Managed ? "switched" : "not switched")}, plugged in {ds.PluggedInHz} Hz, battery {ds.BatteryHz} Hz");
            }
            return sb.ToString();
        }
    }
}
