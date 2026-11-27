# Refresh Rate Switcher – hardware scenario tests (GitHub Actions Windows runners only)
# Copyright (C) 2026 Noel Chan
# SPDX-License-Identifier: GPL-3.0-or-later
#
# Runs the real app against virtual monitors (virtual-displays.ps1) with a simulated power source
# (the app's test-only --test-power switch), and checks what Windows actually ends up showing:
#   plug in / unplug, battery-% updates, a rate picked in Windows, wake from sleep, a full-screen
#   game changing the resolution, a second identical monitor plugged in and out, the settings window.
# Results, the app's event log and screenshots go to test-results-hardware\.

param([string]$Exe = 'bin\Release\net48\RefreshRateSwitcher.exe')

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\virtual-displays.ps1"

$results = Join-Path $PWD 'test-results-hardware'
New-Item -ItemType Directory -Force $results | Out-Null
$Exe = (Resolve-Path $Exe).Path
$settingsKey = 'HKCU:\Software\RefreshRateSwitcher'
$powerFile = Join-Path $results 'power.txt'
$eventLog = Join-Path $results 'power.log'
$errorLog = Join-Path $env:TEMP 'RefreshRateSwitcher-errors.log'
$script:failures = 0
$script:seq = 0
$summary = New-Object System.Collections.Generic.List[string]

function Check([bool]$ok, [string]$what) {
  $line = $(if ($ok) { 'PASS  ' } else { 'FAIL  ' }) + $what
  Write-Host $line; $summary.Add($line)
  if (-not $ok) { $script:failures++ }
}
function Note([string]$text) { Write-Host "      $text"; $summary.Add("      $text") }
function Show([string]$title) {
  Note "-- $title"
  Get-DisplayModes | ForEach-Object { Note ("{0} {1,-24} now {2,-14} saved {3,-14} rates {4}" -f $_.Device, $_.Adapter, $_.Current, $_.Saved, $_.Rates) }
}

function Wait-Until([scriptblock]$cond, [int]$seconds = 15) {
  for ($i = 0; $i -lt $seconds * 4; $i++) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 250 }
  return [bool](& $cond)
}
function Virtual { @(Get-VirtualDisplays) }
function VHz { (Virtual | ForEach-Object Hz) -join ',' }

# Simulated power events: "ac", "battery", "<state> tick" (battery-% update), "<state> resume" (woke)
function Power([string]$text) {
  $script:seq++
  for ($i = 0; $i -lt 20; $i++) {   # the app may be reading the file at that moment
    try { [IO.File]::WriteAllText($powerFile, "$text $($script:seq)"); return } catch { Start-Sleep -Milliseconds 100 }
  }
  throw "couldn't write $powerFile"
}
function LogText { if (Test-Path $eventLog) { Get-Content $eventLog -Raw } else { '' } }
function LogLinesSince([int]$n) { @(Get-Content $eventLog -ErrorAction SilentlyContinue | Select-Object -Skip $n) }
function LogHas([int]$n, [string]$pattern) { [bool](@(LogLinesSince $n) -match $pattern).Count }
function LogCount { @(Get-Content $eventLog -ErrorAction SilentlyContinue).Count }

function AppProcs { @(Get-Process RefreshRateSwitcher -ErrorAction SilentlyContinue) }
function StartApp { Start-Process $Exe -ArgumentList '--background', '--test-power', "`"$powerFile`""; Start-Sleep 4 }
function StopApp {
  try { $e = [Threading.EventWaitHandle]::OpenExisting('Local\RefreshRateSwitcher_Exit'); $e.Set() | Out-Null; $e.Dispose() } catch { }
  for ($i = 0; $i -lt 20 -and (AppProcs).Count -gt 0; $i++) { Start-Sleep -Milliseconds 250 }
  AppProcs | Stop-Process -Force -ErrorAction SilentlyContinue
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class W32 {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
}
'@
function Screenshot([string]$name) {
  $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
  $bmp.Save((Join-Path $results "$name.png")); $g.Dispose(); $bmp.Dispose()
}

function SavedDisplays {
  @(Get-ChildItem "$settingsKey\Displays" -ErrorAction SilentlyContinue | ForEach-Object { Get-ItemProperty $_.PSPath })
}

# ------------------------------------------------------------------ setup
Write-Host "`n== Setup: one virtual monitor (60/120/144/165 Hz) next to the runner's own display"
StopApp
Remove-Item $settingsKey -Recurse -ErrorAction SilentlyContinue
Remove-Item $errorLog, $eventLog -ErrorAction SilentlyContinue
Check ([bool](Install-VirtualDisplays -Count 1 -Rates 60, 120, 144, 165 | Select-Object -Last 1)) 'virtual monitor installed'
Show 'displays'
Set-Content $powerFile 'ac' -NoNewline

# First start creates settings; external monitors start out "not switched"
StartApp
Check ((AppProcs).Count -eq 1) 'app started with the simulated power source'
StopApp
$saved = SavedDisplays
$saved | ForEach-Object { Note "saved: $($_.Key) '$($_.Name)' switched=$($_.Managed) $($_.PluggedInHz)/$($_.BatteryHz) Hz port=$($_.Port)" }
$vEntry = $saved | Where-Object { $_.Managed -eq 0 } | Select-Object -First 1
Check ($null -ne $vEntry) 'virtual monitor saved as "not switched" (external monitors are left alone by default)'
Check ((VHz) -eq '60') "…and its rate wasn't touched: $(VHz) Hz"

# Turn it on, as ticking "Switch this display" would: 165 Hz plugged in, 60 Hz on battery
$vKey = $vEntry.PSPath
Set-ItemProperty $vKey Managed 1; Set-ItemProperty $vKey PluggedInHz 165; Set-ItemProperty $vKey BatteryHz 60

# ------------------------------------------------------------------ power
Write-Host "`n== Power source"
StartApp
Check (Wait-Until { (VHz) -eq '165' }) "start on AC: set to 165 Hz (now $(VHz))"
$n = LogCount
Check ((LogText) -notmatch 'notify Warning') 'no warnings at startup (the runner display offers only one rate)'

Power 'battery'
Check (Wait-Until { (VHz) -eq '60' }) "unplug: 60 Hz (now $(VHz))"
Start-Sleep 1
Check (LogHas $n 'notify Info: On battery') 'unplug: "On battery" notification shown'

$n = LogCount
Power 'battery tick'
Start-Sleep 4
Check ((VHz) -eq '60') "battery-% update: rate unchanged ($(VHz))"
Check (-not (LogHas $n 'notify')) 'battery-% update: no notification'

# A rate picked in Windows Settings stays until the power source changes
$d = Virtual | Select-Object -First 1
[Disp]::SetMode($d.Device, $d.Width, $d.Height, 144, 1) | Out-Null
Start-Sleep 4
Check ((VHz) -eq '144') "rate picked in Windows (144 Hz) is left alone after the display-change event ($(VHz))"
Power 'battery tick'
Start-Sleep 4
Check ((VHz) -eq '144') "…and after a battery-% update ($(VHz))"

$n = LogCount
Power 'ac'
Check (Wait-Until { (VHz) -eq '165' }) "plug in: 165 Hz (now $(VHz))"
Start-Sleep 1
Check (LogHas $n 'notify Info: Plugged in') 'plug in: "Plugged in" notification shown'

Power 'battery'
Check (Wait-Until { (VHz) -eq '60' }) "unplug again: 60 Hz (now $(VHz))"
Power 'ac'
Check (Wait-Until { (VHz) -eq '165' }) "plug in again: 165 Hz (now $(VHz))"

# ------------------------------------------------------------------ sleep
Write-Host "`n== Wake from sleep"
# Some drivers reset the rate during sleep; the app sets it again on wake
$d = Virtual | Select-Object -First 1
[Disp]::SetMode($d.Device, $d.Width, $d.Height, 60, 1) | Out-Null
Start-Sleep 3
Power 'ac resume'
Check (Wait-Until { (VHz) -eq '165' } 15) "wake on AC after the driver reset the rate: back to 165 Hz (now $(VHz))"

# Unplugged while asleep: Windows reports the new power source on wake
Power 'battery resume'
Check (Wait-Until { (VHz) -eq '60' } 15) "woke on battery after being unplugged while asleep: 60 Hz (now $(VHz))"

# ------------------------------------------------------------------ full-screen game
Write-Host "`n== Full-screen game changes the resolution (temporary mode), on battery"
Show 'before the game'
$d = Virtual | Select-Object -First 1
$desktopRes = "$($d.Width)x$($d.Height)"
[Disp]::SetMode($d.Device, 1280, 720, 165, 0) | Out-Null       # temporary, like an exclusive full-screen game
Start-Sleep 5
Show 'game running (1280x720 @ 165 requested)'
$g = Virtual | Select-Object -First 1
Note "while the game runs: now $($g.Current), saved $($g.Saved)"
[Disp]::Restore() | Out-Null                                   # the game exits: Windows restores the saved modes
Start-Sleep 5
Show 'after the game exits'
$after = Virtual | Select-Object -First 1
Check ($after.Saved -like "$desktopRes@*") "game's temporary resolution not saved as the desktop resolution (saved: $($after.Saved), desktop was $desktopRes)"
Check ("$($after.Width)x$($after.Height)" -eq $desktopRes) "desktop resolution back to $desktopRes after the game (now $($after.Current))"
# Put things back for the next tests whatever happened
[Disp]::SetMode($after.Device, 1920, 1080, 60, 1) | Out-Null
Start-Sleep 3

# ------------------------------------------------------------------ monitors plugged in and out
Write-Host "`n== A second, identical monitor is plugged in and out (on AC)"
Power 'ac'
Check (Wait-Until { (VHz) -eq '165' }) "on AC: 165 Hz (now $(VHz))"
$before = SavedDisplays
Set-VirtualDisplayCount 2 | Out-Null
Start-Sleep 6
Show 'two identical monitors'
$hz = @(Virtual | ForEach-Object Hz | Sort-Object)
Check ($hz.Count -eq 2 -and ($hz -join ',') -eq '60,165') "first monitor keeps its 165 Hz, the new one is left alone at 60 Hz ($($hz -join ', '))"
$now = SavedDisplays
Check ($now.Count -eq $before.Count + 1) "settings added for the new monitor only ($($before.Count) → $($now.Count))"
Check (@($now | Where-Object { $_.Managed -eq 1 -and $_.PluggedInHz -eq 165 }).Count -eq 1) 'exactly one monitor has the 165/60 Hz settings'
$now | ForEach-Object { Note "saved: $($_.Key) switched=$($_.Managed) $($_.PluggedInHz)/$($_.BatteryHz) Hz port=$($_.Port)" }

Power 'battery'
Check (Wait-Until { ((Virtual | ForEach-Object Hz | Sort-Object) -join ',') -eq '60,60' }) "unplug with two monitors: switched one goes to 60 Hz ($(VHz))"
Power 'ac'
Check (Wait-Until { ((Virtual | ForEach-Object Hz | Sort-Object) -join ',') -eq '60,165' }) "plug in with two monitors: only the switched one goes to 165 Hz ($(VHz))"

Write-Host "`n== Settings window with three displays"
Start-Process $Exe -ArgumentList '--test-power', "`"$powerFile`"" -Wait   # second launch opens the running copy's window
Start-Sleep 3
Screenshot 'settings-window-3-displays'
$w = [W32]::FindWindow([NullString]::Value, 'Refresh Rate Switcher')
Check ($w -ne [IntPtr]::Zero) 'settings window opens with three displays'
if ($w -ne [IntPtr]::Zero) { [W32]::PostMessage($w, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep 2 }

Set-VirtualDisplayCount 1 | Out-Null
Start-Sleep 6
Show 'second monitor unplugged'
Check ((VHz) -eq '165') "remaining monitor still switched at 165 Hz on AC ($(VHz))"
Check ((SavedDisplays).Count -eq $now.Count) 'unplugging keeps both monitors'' settings for next time'

# ------------------------------------------------------------------ wrap up
Write-Host "`n== Wrap up"
Check ((AppProcs).Count -eq 1) 'app still running after all scenarios'
StopApp
Check ((AppProcs).Count -eq 0) 'app exits when asked'
Check (-not (Test-Path $errorLog)) 'no errors logged by the app'
if (Test-Path $errorLog) { Get-Content $errorLog | Write-Host; Copy-Item $errorLog $results }
Note "app event log:"
Get-Content $eventLog -ErrorAction SilentlyContinue | ForEach-Object { Note "  $_" }
$summary | Set-Content (Join-Path $results 'hardware-tests.txt')

Write-Host ''
if ($script:failures -gt 0) { Write-Host "$($script:failures) failure(s)"; exit 1 }
Write-Host 'All hardware scenario tests passed'
