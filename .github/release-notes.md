Windows tray app that sets the display refresh rate automatically when you plug in or unplug.

### What's new in 1.2.0
- **Multiple displays**: each display has its own plugged-in and battery rates, and can be switched or left alone.
  Settings follow the monitor, even if you plug it into a different port.
- **Settings window**: open it from the tray menu or the Start Menu. The tray icon and quick menu are still there.
- **About**: version, licence, and links to the latest release and the source code.
- **Installer option** to choose whether the app starts with Windows (`/NOSTARTUP` for silent installs).
- Upgrading keeps your settings: your 1.1.x rates move to the display they controlled. Other displays start out not switched.

### Install
Download **RefreshRateSwitcher-Setup-1.2.0.exe** and run it. It installs just for your account (no admin), adds a Start Menu entry, and appears in **Settings → Apps → Installed apps** for uninstalling.

Windows SmartScreen may warn because the installer isn't code-signed: click **More info → Run anyway**. Some antivirus products may also flag it; this is a false positive. The installer is built by GitHub Actions directly from this repository's source and tested on Windows before release.

### Features
- Separate refresh rate for **plugged in** and **on battery** for each display, chosen from the rates it supports
- Master **auto-switch** toggle
- Lives in the tray; the icon shows the current Hz (blue = AC, amber = battery, grey = disabled)
- Optional start with Windows; re-applies after sleep/resume and when a monitor is connected
- Light: about 9 MB of memory, no CPU use while idle, no internet access

**Not compatible with Windows 11 Dynamic refresh rate (DRR):** setting a fixed rate turns DRR off. See the README's Known limitations.

Requires Windows 10 (1903+) or 11 (.NET Framework 4.8, already built in). Licensed under GNU GPL v3 (or later).
