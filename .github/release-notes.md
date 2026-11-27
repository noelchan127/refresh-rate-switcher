Windows tray app that sets the display refresh rate automatically when you plug in or unplug.

### What's new in 1.2.3
Fixes and small improvements:
- **Monitors that briefly disconnect are switched again.** A dock reconnecting, a monitor switching inputs or a graphics driver update could leave a monitor at its default rate. It's now set back to your chosen rate.
- **Waking from sleep is more reliable.** A battery-percentage update straight after waking could stop the app from setting the rate again. If you plug in or unplug while the PC is asleep, you now also get the notification when it wakes.
- **Start with Windows** now follows the on/off switch in Task Manager → Startup apps (and Settings → Apps → Startup).
- **Changing a rate in the app only affects that display**, and only for the power state you changed, so a rate you picked in Windows for another display stays.
- **New monitors** can be set up the moment they appear.
- **Settings window:** updates when you change resolution, lets you choose rates while auto-switch is off, and says when the rate was changed outside the app.
- **Tray menu:** with one display, switching can be turned off as well as on.
- **More robust:** damaged settings no longer stop the app from starting, start-up problems are logged, and displays keep their identity while the setup is changing. Interlaced modes are no longer offered.
- **About:** copyright line, the licence opens the copy installed with the app, and a link to report problems privately.
- **Installer:** closes a running copy wherever it was installed, cleans up the old folder if you install somewhere else, and no longer installs an unused icon file.
- **Check your download:** each release now lists the installer's SHA-256 checksum in `SHA256SUMS.txt` and carries a GitHub record that it was built here (see the README).

### Fixed in 1.2.2
- **Identical monitors keep their own settings.** Plugging in a second monitor of the same model no longer makes the first one forget its settings, and a lone monitor no longer takes over another one's. Identical monitors are told apart by the port they were last on, so swapping their cables swaps their settings.
- **Slow display drivers:** if a driver hadn't reported its refresh rates yet (e.g. right after docking or waking), the display stayed unswitched until you picked rates yourself. Its rates are now filled in once the driver reports them, and the settings window shows them too.
- **A rate you pick in Windows Settings now stays** until you plug in, unplug, or wake the PC. Battery-percentage updates no longer undo it.
- **Refused changes are retried.** If the driver refuses a change (e.g. while docking), it's tried again a few times instead of being skipped.
- **Setup closes the app politely.** Setup now asks the running app to exit instead of force-closing every program with the same file name. Only 1.2.1 and older, which don't understand that request, are stopped directly, and only the copy in the install folder.
- **Quicker when displays change:** each event no longer repeats the same display queries, and the tray icon is only redrawn when it changes.

### New in 1.2.1
- The publisher and author are now shown as **Noel Chan** in Windows Settings → Apps, the installer and app file properties, and the About window.

### New in 1.2.0
- **Multiple displays**: each display has its own plugged-in and battery rates, and can be switched or left alone.
  Settings follow the monitor, even if you plug it into a different port.
- **Settings window**: open it from the tray menu or the Start Menu. The tray icon and quick menu are still there.
- **About**: version, licence, and links to the latest release and the source code.
- **Installer option** to choose whether the app starts with Windows (`/NOSTARTUP` for silent installs).
- Upgrading keeps your settings: your 1.1.x rates move to the display they controlled. Other displays start out not switched.

### Install
Download **RefreshRateSwitcher-Setup-1.2.3.exe** and run it. It installs just for your account (no admin), adds a Start Menu entry, and appears in **Settings → Apps → Installed apps** for uninstalling.

Windows SmartScreen may warn because the installer isn't code-signed: click **Run anyway** (on some versions of Windows, click **More info** first). Some antivirus products may also flag unsigned installers. The installer is built by GitHub Actions directly from this repository's source and tested on Windows before release. Problems and suggestions: use the repository's private reporting form (Security tab → Report a vulnerability).

### Features
- Separate refresh rate for **plugged in** and **on battery** for each display, chosen from the rates it supports
- Master **auto-switch** toggle
- Lives in the tray; the icon shows the current Hz (blue = AC, amber = battery, grey = disabled)
- Optional start with Windows; re-applies after sleep/resume and when a monitor is connected
- Lightweight, with very low resource use and no internet access

**Not compatible with Windows 11 Dynamic refresh rate (DRR):** setting a fixed rate turns DRR off. See the README's Known limitations.

Requires Windows 10 (1903+) or 11 (.NET Framework 4.8, already built in). Licensed under GNU GPL v3 (or later).
