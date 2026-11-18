# Refresh Rate Switcher

Windows tray app that sets the display refresh rate automatically depending on whether
the laptop is plugged in or on battery.

## Install
Run `RefreshRateSwitcher-Setup-1.1.0.exe`. It installs per-user (no admin) to
`%LOCALAPPDATA%\Programs\Refresh Rate Switcher`, adds a Start Menu entry, and registers
in Settings → Apps → Installed apps. Running the setup again upgrades in place and keeps your settings.

On first run the app picks max Hz for plugged in, 60 Hz (or the lowest) for battery,
and enables "Start with Windows".

## Use
Click the tray icon (number = current Hz; blue = plugged in, amber = battery, grey = disabled),
or open it from the Start Menu while it's running to pop up the same menu:
- **Auto-switch enabled**: master on/off toggle
- **Plugged in ▸ / On battery ▸**: pick the rate for each state
- **Display ▸**: Automatic = built-in laptop panel (falls back to primary display)
- **Start with Windows**, **Show notifications**, **Exit**

## Uninstall
Settings → Apps → Installed apps → Refresh Rate Switcher → Uninstall.
This closes the app and removes files, the Start Menu entry, the startup entry and saved settings.

## Build from source
`dotnet build -c Release` (needs the .NET SDK; the .NET Framework 4.8 reference assemblies come from NuGet automatically).

Installer: `makensis installer.nsi` (NSIS 3) after building; output goes to `out\`.

## License
Copyright (C) 2026 noelchan127

This program is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License as published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version. It is distributed WITHOUT ANY WARRANTY; see
[LICENSE](LICENSE) for the full terms.

