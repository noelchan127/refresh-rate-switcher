# Refresh Rate Switcher – end-to-end tests on a real Windows machine (run by GitHub Actions).
# Copyright (C) 2026 noelchan127
# SPDX-License-Identifier: GPL-3.0-or-later
#
# Covers: built-in self-test, silent install/uninstall, the "start with Windows" choice,
# upgrading from the previous release, single-instance behaviour, the settings window,
# and idle memory / CPU use. Results and screenshots go to test-results\.

$ErrorActionPreference = 'Stop'
$results = Join-Path $PWD 'test-results'
New-Item -ItemType Directory -Force $results | Out-Null
$exeName = 'RefreshRateSwitcher.exe'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\Refresh Rate Switcher'
$installedExe = Join-Path $installDir $exeName
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\RefreshRateSwitcher'
$settingsKey = 'HKCU:\Software\RefreshRateSwitcher'
$setup = (Get-ChildItem out -Filter 'RefreshRateSwitcher-Setup-*.exe' | Select-Object -First 1).FullName
$Version = [IO.Path]::GetFileNameWithoutExtension($setup) -replace '^RefreshRateSwitcher-Setup-', ''
$script:failures = 0
$summary = New-Object System.Collections.Generic.List[string]

function Check([bool]$ok, [string]$what) {
  $line = $(if ($ok) { 'PASS  ' } else { 'FAIL  ' }) + $what
  Write-Host $line
  $summary.Add($line)
  if (-not $ok) { $script:failures++ }
}
function Note([string]$text) { Write-Host "      $text"; $summary.Add("      $text") }

function RunValue { (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).RefreshRateSwitcher }
function AppProcs { @(Get-Process RefreshRateSwitcher -ErrorAction SilentlyContinue) }
function StopApp { AppProcs | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep 1 }

function Install([string]$file, [string[]]$extra = @()) {
  $p = Start-Process $file -ArgumentList (@('/S') + $extra) -Wait -PassThru
  return $p.ExitCode
}
function Uninstall {
  StopApp
  if (-not (Test-Path "$installDir\Uninstall.exe")) { return }
  Start-Process "$installDir\Uninstall.exe" -ArgumentList '/S' -Wait
  # The NSIS uninstaller re-launches itself from %TEMP%; wait for it to finish
  for ($i = 0; $i -lt 60 -and (Test-Path $uninstKey); $i++) { Start-Sleep -Milliseconds 500 }
  Start-Sleep 1
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class W32 {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

function Screenshot([string]$name) {
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
  $h = [W32]::FindWindow([NullString]::Value, 'Refresh Rate Switcher')
  if ($h -ne [IntPtr]::Zero) {
    $r = New-Object W32+RECT
    [W32]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = [Math]::Min($r.Right, $b.Width) - [Math]::Max($r.Left, 0)
    $hh = [Math]::Min($r.Bottom, $b.Height) - [Math]::Max($r.Top, 0)
    if ($w -gt 0 -and $hh -gt 0) {
      $crop = $bmp.Clone((New-Object System.Drawing.Rectangle ([Math]::Max($r.Left, 0)), ([Math]::Max($r.Top, 0)), $w, $hh), $bmp.PixelFormat)
      $crop.Save((Join-Path $results "$name-window.png"))
      $crop.Dispose()
    }
  }
  $bmp.Save((Join-Path $results "$name-desktop.png"))
  $g.Dispose(); $bmp.Dispose()
}

# "Memory" in Task Manager's Processes tab = private working set
function PrivateWS([System.Diagnostics.Process]$p) {
  try { (Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess=$($p.Id)").WorkingSetPrivate } catch { 0 }
}
function Usage([System.Diagnostics.Process]$p) {
  $p.Refresh()
  '{0:N1} MB in Task Manager (private working set), {1:N1} MB working set, {2} threads, {3} handles' -f ((PrivateWS $p) / 1MB), ($p.WorkingSet64 / 1MB), $p.Threads.Count, $p.HandleCount
}
$errorLog = Join-Path $env:TEMP 'RefreshRateSwitcher-errors.log'
Remove-Item $errorLog -ErrorAction SilentlyContinue

# ------------------------------------------------------------------ machine info
$os = Get-CimInstance Win32_OperatingSystem
Note "Runner: $($os.Caption) $($os.Version), $([Environment]::ProcessorCount) CPUs"
Get-CimInstance Win32_VideoController | ForEach-Object { Note "GPU: $($_.Name), $($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution) @ $($_.CurrentRefreshRate) Hz" }
Note "Power: $([System.Windows.Forms.SystemInformation]::PowerStatus.PowerLineStatus)"

# ------------------------------------------------------------------ 1. built-in self-test
Write-Host "`n== Built-in self-test"
$p = Start-Process "bin\Release\net48\$exeName" -ArgumentList '--selftest', "`"$results`"" -Wait -PassThru
Get-Content "$results\selftest.txt" | Write-Host
Check ($p.ExitCode -eq 0) "self-test exit code $($p.ExitCode)"

# ------------------------------------------------------------------ 2. fresh install, default options
Write-Host "`n== Fresh silent install"
StopApp
Remove-Item $settingsKey -Recurse -ErrorAction SilentlyContinue
Remove-ItemProperty $runKey RefreshRateSwitcher -ErrorAction SilentlyContinue
Check ((Install $setup) -eq 0) 'installer exit code 0'
foreach ($f in $exeName, "$exeName.config", 'app.ico', 'LICENSE.txt', 'Uninstall.exe') {
  Check (Test-Path (Join-Path $installDir $f)) "installed $f"
}
$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Refresh Rate Switcher.lnk'
Check (Test-Path $lnk) 'Start Menu shortcut created'
$u = Get-ItemProperty $uninstKey -ErrorAction SilentlyContinue
Check ($u.DisplayVersion -eq $Version) "Installed apps entry shows version $($u.DisplayVersion)"
Check ((RunValue) -eq "`"$installedExe`" --background") "starts with Windows by default: $(RunValue)"

# ------------------------------------------------------------------ 3. running app
Write-Host "`n== Running in the background"
Start-Process $installedExe -ArgumentList '--background'
Start-Sleep 6
$procs = AppProcs
Check ($procs.Count -eq 1) "app is running ($($procs.Count) process)"
$app = $procs[0]
Note ('idle in tray: ' + (Usage $app))
$cpu0 = $app.TotalProcessorTime; Start-Sleep 20; $app.Refresh()
$cpuMs = ($app.TotalProcessorTime - $cpu0).TotalMilliseconds
Note ("CPU over 20 s idle: {0:N0} ms" -f $cpuMs)
Check ($cpuMs -lt 200) 'idle CPU use is negligible'
$idlePrivate = PrivateWS $app
Check ($idlePrivate -gt 0 -and $idlePrivate -lt 20MB) 'idle memory (Task Manager) under 20 MB'
$displays = @(Get-ChildItem "$settingsKey\Displays" -ErrorAction SilentlyContinue)
Check ($displays.Count -ge 1) "settings saved for $($displays.Count) display(s)"
$displays | ForEach-Object { $v = Get-ItemProperty $_.PSPath; Note "display '$($v.Name)' id=$($v.Key) switched=$($v.Managed) $($v.PluggedInHz)/$($v.BatteryHz) Hz" }

Write-Host "`n== Second launch opens the settings window"
Start-Process $installedExe -Wait
Start-Sleep 4
Check ((AppProcs).Count -eq 1) 'still a single instance'
$h = [W32]::FindWindow([NullString]::Value, 'Refresh Rate Switcher')
Check ($h -ne [IntPtr]::Zero) 'settings window opened'
Screenshot 'installed-app'
Note ('with window open: ' + (Usage $app))

[W32]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null   # WM_CLOSE
Start-Sleep 3
Check ([W32]::FindWindow([NullString]::Value, 'Refresh Rate Switcher') -eq [IntPtr]::Zero) 'window closed'
Check ((AppProcs).Count -eq 1) 'app keeps running in the tray after the window closes'
Start-Sleep 2
Note ('after closing window: ' + (Usage $app))
Check ((PrivateWS $app) -le $idlePrivate * 1.5) 'memory returns close to the idle level after closing the window'
Screenshot 'after-close'

# Open and close the window repeatedly: memory and handles must not keep growing
$app.Refresh(); $h0 = $app.HandleCount
for ($i = 1; $i -le 6; $i++) {
  Start-Process $installedExe -Wait; Start-Sleep 2
  $hw = [W32]::FindWindow([NullString]::Value, 'Refresh Rate Switcher')
  [W32]::PostMessage($hw, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep 2
  if ($i -eq 1) { $app.Refresh(); $h1 = $app.HandleCount; $m1 = PrivateWS $app }
}
Start-Sleep 2; $app.Refresh()
Note ("after 6 open/close cycles: " + (Usage $app) + " (after 1st cycle: $h1 handles)")
Check ($app.HandleCount -le $h1 + 15) 'no handle leak from opening/closing the window'
Check ((PrivateWS $app) -le [Math]::Max($m1, $idlePrivate) * 1.5) 'no memory growth from opening/closing the window'

Start-Process $installedExe -ArgumentList '--background' -Wait
Check ((AppProcs).Count -eq 1) 'a second --background launch exits quietly'
Check ([W32]::FindWindow([NullString]::Value, 'Refresh Rate Switcher') -eq [IntPtr]::Zero) '…without opening the window'

# ------------------------------------------------------------------ 4. uninstall
Write-Host "`n== Uninstall"
Uninstall
Check (-not (Test-Path $installDir)) 'install folder removed'
Check (-not (Test-Path $lnk)) 'Start Menu shortcut removed'
Check (-not (Test-Path $uninstKey)) 'Installed apps entry removed'
Check ($null -eq (RunValue)) 'startup entry removed'
Check (-not (Test-Path $settingsKey)) 'settings removed'

# ------------------------------------------------------------------ 5. "start with Windows" unticked, and kept on upgrade
Write-Host "`n== Install without startup"
Install $setup @('/NOSTARTUP') | Out-Null
Check ($null -eq (RunValue)) '/NOSTARTUP: no startup entry'
Start-Process $installedExe -ArgumentList '--background'; Start-Sleep 5; StopApp
Check ($null -eq (RunValue)) 'app does not add a startup entry by itself'
Install $setup | Out-Null
Check ($null -eq (RunValue)) 'reinstall keeps the earlier choice (still off)'
Install $setup @('/STARTUP') | Out-Null
Check ((RunValue) -eq "`"$installedExe`" --background") '/STARTUP turns it on'
Install $setup | Out-Null
Check ($null -ne (RunValue)) 'reinstall keeps the earlier choice (still on)'
Uninstall

# ------------------------------------------------------------------ 6. upgrade from the previous release
Write-Host "`n== Upgrade from 1.1.3"
$old = Join-Path $env:RUNNER_TEMP 'old'
New-Item -ItemType Directory -Force $old | Out-Null
try {
  gh release download v1.1.3 --repo $env:GITHUB_REPOSITORY --pattern '*.exe' --dir $old --clobber
  $oldSetup = Get-ChildItem $old -Filter '*Setup*.exe' | Select-Object -First 1
  Install $oldSetup.FullName | Out-Null
  Start-Process $installedExe; Start-Sleep 6           # 1.1.3 first run: saves settings, adds startup entry
  Note ('1.1.3 for comparison: ' + (Usage (AppProcs)[0]))
  StopApp
  Note "1.1.3 startup entry: $(RunValue)"
  $legacy = Get-ItemProperty $settingsKey
  Note "1.1.3 settings: $($legacy.PluggedInHz)/$($legacy.BatteryHz) Hz, display $($legacy.Display)"
  Set-ItemProperty $settingsKey PluggedInHz 61   # recognisable values to check the migration
  Set-ItemProperty $settingsKey BatteryHz 59

  Install $setup | Out-Null
  Check ((RunValue) -eq "`"$installedExe`" --background") 'upgrade updates the startup entry to tray-only'
  Start-Process $installedExe -ArgumentList '--background'; Start-Sleep 6; StopApp
  $migrated = @(Get-ChildItem "$settingsKey\Displays" | ForEach-Object { Get-ItemProperty $_.PSPath } | Where-Object { $_.Managed -eq 1 })
  Check ($migrated.Count -eq 1 -and $migrated[0].PluggedInHz -eq 61 -and $migrated[0].BatteryHz -eq 59) `
        "1.1.3 rates carried over to the display it controlled ($($migrated.Count) switched display)"
  Uninstall
  Check (-not (Test-Path $settingsKey) -and $null -eq (RunValue)) 'uninstall after upgrade leaves nothing behind'
} catch {
  Check $false "upgrade test: $_"
}

# ------------------------------------------------------------------ result
Check (-not (Test-Path $errorLog)) 'no errors logged by the app'
if (Test-Path $errorLog) { Get-Content $errorLog | Write-Host }
$summary | Set-Content (Join-Path $results 'install-tests.txt')

# Print the screenshots as base64 so they can be read from the log as well as the artifact
foreach ($img in Get-ChildItem $results -Filter *.png) {
  $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($img.FullName))
  Write-Host "##IMG-BEGIN $($img.Name)"
  for ($i = 0; $i -lt $b64.Length; $i += 4000) { Write-Host ('##IMG ' + $b64.Substring($i, [Math]::Min(4000, $b64.Length - $i))) }
  Write-Host "##IMG-END"
}
Write-Host ''
if ($script:failures -gt 0) { Write-Host "$($script:failures) failure(s)"; exit 1 }
Write-Host 'All Windows tests passed'
