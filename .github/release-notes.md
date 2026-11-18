Windows tray app that sets the display refresh rate automatically when you plug in or unplug.

### What's new in 1.1.2
- Documented that the app is **not compatible with Windows 11 Dynamic refresh rate (DRR)**, and conflicts with laptop vendor tools that also switch refresh rate.
- Explained the SmartScreen / antivirus warnings shown for the unsigned installer.
- No functional changes since 1.1.1. Installing over an earlier version keeps your settings. Licensed under GNU GPL v3 (or later).

### Install
Download **RefreshRateSwitcher-Setup-1.1.2.exe** and run it. It installs just for your account (no admin), adds a Start Menu entry, and appears in **Settings → Apps → Installed apps** for uninstalling.

Windows SmartScreen may warn because the installer isn't code-signed: click **More info → Run anyway**. Some antivirus products may also flag it; this is a false positive. The installer is built by GitHub Actions directly from this repository's source.

### Features
- Separate refresh rate for **plugged in** and **on battery**, chosen from the rates your display supports
- Master **auto-switch** toggle
- Targets the built-in laptop panel automatically (or pick a display)
- Starts with Windows, lives in the tray; the icon shows the current Hz (blue = AC, amber = battery, grey = disabled)
- Re-applies after sleep/resume

**Not compatible with Windows 11 Dynamic refresh rate (DRR):** setting a fixed rate turns DRR off. See the README's Known limitations.

Requires Windows 10 (1903+) or 11 (.NET Framework 4.8, already built in).
