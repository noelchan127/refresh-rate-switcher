# Refresh Rate Switcher

Windows tray app that sets the display refresh rate automatically depending on whether
the laptop is plugged in or on battery. Works with multiple displays, each with its own settings.

<p align="center"><img src="docs/screenshots/tray-menu.png" width="420" alt="Tray menu with the Plugged in submenu open"></p>

## Install
Download `RefreshRateSwitcher-Setup-<version>.exe` from the [latest release](../../releases/latest) and run it. It installs per-user (no admin) to
`%LOCALAPPDATA%\Programs\Refresh Rate Switcher`, adds a Start Menu entry, and registers
in Settings → Apps → Installed apps. Running the setup again upgrades in place and keeps your settings.

<img src="docs/screenshots/start-menu.png" width="110" alt="Start Menu entry" align="right">

Setup asks whether to **start the app when you sign in to Windows** (recommended; you can change it later).
For silent installs, use `/S`, and add `/NOSTARTUP` to skip the startup entry.

On first run, the built-in laptop screen is set to switch between its highest rate when plugged in and
60 Hz (or its lowest rate) on battery. External monitors are left alone until you turn them on in Settings.

### Security warnings
The installer and app are **not code-signed**, so Windows may warn you the first time:
- **SmartScreen** ("Windows protected your PC"): click **More info → Run anyway**.
- **Antivirus**: some products may flag unsigned installers like this one, partly because setup closes a
  running copy of the app before upgrading. This is a false positive.

Every release is built by [GitHub Actions](../../actions/workflows/release.yml) directly from the source in
this repository, so you can see exactly what went into it. If you'd rather not trust a pre-built binary,
[build it from source](#build-from-source).

## Use
The tray icon shows the current refresh rate: **blue** when plugged in, **amber** on battery, **grey** when auto-switch is off.
Hover over it to see both settings.

| Plugged in | On battery | Tooltip |
|:---:|:---:|:---:|
| <img src="docs/screenshots/tray-icon-plugged-in.png" width="150" alt="Blue 120 Hz tray icon"> | <img src="docs/screenshots/tray-icon-battery.png" width="190" alt="Amber 60 Hz tray icon"> | <img src="docs/screenshots/tooltip.png" width="300" alt="Tooltip showing current rate and settings"> |

Click the icon to show the quick menu:
- **Settings…**: opens the settings window
- **Auto-switch enabled**: master on/off toggle
- **Plugged in ▸ / On battery ▸**: pick the rate for each state. With more than one display,
  each display gets its own submenu, including **Switch this display**.
- **Start with Windows**, **Show notifications**, **About**, **Exit**

### Settings window
Open it from **Settings…** in the tray menu, or from the Start Menu. Each connected display has its own
section: tick **Switch this display automatically** and choose its plugged-in and battery rates.
Changes apply immediately. Closing the window keeps the app running in the tray.

Settings follow the monitor rather than the port, so a monitor keeps its settings when you plug it into a
different port or dock. Two identical monitors are told apart by the port they're connected to.

The **About** tab shows the version, licence and links to the latest release and the source code.
The app itself never connects to the internet, so it doesn't check for updates automatically:
to update, download the latest installer and run it. **Copy system info** copies your display details,
which is useful in a bug report.

When the power source changes it switches the rate and, if notifications are on, tells you:

<img src="docs/screenshots/notification-plugged-in.png" width="380" alt="Notification: Plugged in, refresh rate set to 120 Hz"> <img src="docs/screenshots/notification-battery.png" width="380" alt="Notification: On battery, refresh rate set to 60 Hz">

It's lightweight: about 9 MB of memory and no CPU use while idle, since it only wakes up on power and display events.
The settings window is only created when you open it and is freed again when you close it.

<img src="docs/screenshots/task-manager.png" width="600" alt="Task Manager showing RefreshRateSwitcher using 0% CPU and 8.9 MB memory">

## Uninstall
Settings → Apps → Installed apps → Refresh Rate Switcher → Uninstall.
This closes the app and removes files, the Start Menu entry, the startup entry and saved settings.

## Known limitations
- **Not compatible with Windows 11 Dynamic refresh rate (DRR).** This app sets a *fixed* refresh rate,
  which turns DRR ("Dynamic (xx Hz)" in Settings) off, and Windows offers no way for apps to turn it back on.
  If you rely on DRR, don't use this app (or turn off auto-switch). To restore DRR after uninstalling,
  re-select it in Settings → System → Display → Advanced display → Choose a refresh rate.
- Laptop vendor utilities (e.g. Armoury Crate, Lenovo Vantage, Dell/HP tools) that also change the refresh rate
  on power events will conflict with this app. Turn that feature off in the vendor tool.
- Displays set to **Duplicate** share one refresh rate, so they appear as a single entry.
- If you change a display's rate yourself in Windows Settings, the app leaves it until the next time you
  plug in or unplug.

## Build from source
`dotnet build -c Release` (needs the .NET SDK; the .NET Framework 4.8 reference assemblies come from NuGet automatically).

Installer: `makensis installer.nsi` (NSIS 3) after building; output goes to `out\`.

Tests: `RefreshRateSwitcher.exe --selftest <folder>` checks the Windows display APIs on your machine
(without changing anything), the settings logic and the window layout, and writes a report and preview
images to the folder. GitHub Actions also runs [`.github/scripts/test-windows.ps1`](.github/scripts/test-windows.ps1),
which installs, upgrades, runs and uninstalls the app on Windows for every change.

## Security
To report a security problem, please see [SECURITY.md](SECURITY.md). Don't post it in a public issue.

## License
Copyright (C) 2026 noelchan127

This program is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License as published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version. It is distributed WITHOUT ANY WARRANTY; see
[LICENSE](LICENSE) for the full terms.

