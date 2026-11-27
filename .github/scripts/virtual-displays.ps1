# Refresh Rate Switcher – virtual monitors for CI (GitHub Actions Windows runners only)
# Copyright (C) 2026 Noel Chan
# SPDX-License-Identifier: GPL-3.0-or-later
#
# GitHub's Windows machines have one basic display with a single fixed refresh rate, so the app has
# nothing to switch. This installs the open-source, signed "Virtual Display Driver" (MIT licence,
# https://github.com/VirtualDrivers/Virtual-Display-Driver), which adds monitors that offer several
# refresh rates. It is only ever installed on the throwaway CI machine, never shipped with the app.
#
# Dot-source this file, then:
#   Install-VirtualDisplays -Count 1 -Rates 60,120,144,165
#   Set-VirtualDisplayCount 2        # "plug in" a second (identical) monitor
#   Get-DisplayModes                 # what Windows reports for every display

$ErrorActionPreference = 'Stop'
$script:VddDir = 'C:\VirtualDisplayDriver'

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Disp {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct DEVMODE {
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
    public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
    public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
    public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
    public short dmLogPixels;
    public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
    public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
  }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct DISPLAY_DEVICE {
    public int cb;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
    public int StateFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
  }
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string n, int i, ref DEVMODE d);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevices(string n, uint i, ref DISPLAY_DEVICE d, uint f);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int ChangeDisplaySettingsEx(string n, ref DEVMODE d, IntPtr h, int f, IntPtr l);

  public static DEVMODE Mode(string dev, int i) { var d = new DEVMODE(); d.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)); return EnumDisplaySettings(dev, i, ref d) ? d : default(DEVMODE); }

  public static List<string> Attached() {
    var r = new List<string>();
    for (uint i = 0; ; i++) {
      var dd = new DISPLAY_DEVICE(); dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
      if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
      if ((dd.StateFlags & 1) != 0) r.Add(dd.DeviceName + "|" + dd.DeviceString + "|" + ((dd.StateFlags & 4) != 0 ? "primary" : ""));
    }
    return r;
  }

  public static List<int> Rates(string dev) {
    var cur = Mode(dev, -1); var s = new SortedSet<int>();
    for (int i = 0; ; i++) {
      var d = new DEVMODE(); d.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
      if (!EnumDisplaySettings(dev, i, ref d)) break;
      if (d.dmPelsWidth == cur.dmPelsWidth && d.dmPelsHeight == cur.dmPelsHeight && d.dmDisplayFrequency > 1) s.Add(d.dmDisplayFrequency);
    }
    return new List<int>(s);
  }

  // flags: 0 = temporary (like a full-screen game), 1 = CDS_UPDATEREGISTRY (permanent)
  public static int SetMode(string dev, int w, int h, int hz, int flags) {
    var d = Mode(dev, -1);
    d.dmPelsWidth = w; d.dmPelsHeight = h; d.dmDisplayFrequency = hz;
    d.dmFields = 0x80000 | 0x100000 | 0x400000;
    return ChangeDisplaySettingsEx(dev, ref d, IntPtr.Zero, flags, IntPtr.Zero);
  }

  // Undo temporary changes (what Windows does when a full-screen game exits): re-apply the saved modes
  [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
  static extern int ChangeDisplaySettingsExNull(string n, IntPtr d, IntPtr h, int f, IntPtr l);
  public static int Restore() { return ChangeDisplaySettingsExNull(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero); }
}
'@

function Get-DisplayModes {
  foreach ($a in [Disp]::Attached()) {
    $name, $adapter, $flag = $a -split '\|'
    $cur = [Disp]::Mode($name, -1)      # ENUM_CURRENT_SETTINGS
    $reg = [Disp]::Mode($name, -2)      # ENUM_REGISTRY_SETTINGS
    [pscustomobject]@{
      Device   = $name
      Adapter  = $adapter
      Primary  = $flag -eq 'primary'
      Current  = "$($cur.dmPelsWidth)x$($cur.dmPelsHeight)@$($cur.dmDisplayFrequency)"
      Saved    = "$($reg.dmPelsWidth)x$($reg.dmPelsHeight)@$($reg.dmDisplayFrequency)"
      Hz       = $cur.dmDisplayFrequency
      Width    = $cur.dmPelsWidth
      Height   = $cur.dmPelsHeight
      Rates    = ([Disp]::Rates($name) -join ',')
    }
  }
}

function Get-VirtualDisplays { @(Get-DisplayModes | Where-Object { $_.Adapter -match 'Virtual|MTT|VDD' }) }

function Write-VddSettings([int]$Count, [int[]]$Rates) {
  New-Item -ItemType Directory -Force $script:VddDir | Out-Null
  $g = ($Rates | ForEach-Object { "    <g_refresh_rate>$_</g_refresh_rate>" }) -join "`r`n"
  @"
<?xml version='1.0' encoding='utf-8'?>
<vdd_settings>
  <monitors><count>$Count</count></monitors>
  <gpu><friendlyname>default</friendlyname></gpu>
  <global>
$g
  </global>
  <resolutions>
    <resolution><width>1920</width><height>1080</height><refresh_rate>60</refresh_rate></resolution>
    <resolution><width>1280</width><height>720</height><refresh_rate>60</refresh_rate></resolution>
  </resolutions>
  <logging><SendLogsThroughPipe>false</SendLogsThroughPipe><logging>true</logging><debuglogging>false</debuglogging></logging>
  <colour><SDR10bit>false</SDR10bit><HDRPlus>false</HDRPlus><ColourFormat>RGB</ColourFormat></colour>
  <cursor><HardwareCursor>true</HardwareCursor><CursorMaxX>128</CursorMaxX><CursorMaxY>128</CursorMaxY><AlphaCursorSupport>true</AlphaCursorSupport><XorCursorSupportLevel>2</XorCursorSupportLevel></cursor>
  <edid><CustomEdid>false</CustomEdid><PreventSpoof>false</PreventSpoof><EdidCeaOverride>false</EdidCeaOverride></edid>
</vdd_settings>
"@ | Set-Content -Encoding UTF8 (Join-Path $script:VddDir 'vdd_settings.xml')
}

function Install-VirtualDisplays([int]$Count = 1, [int[]]$Rates = @(60, 120, 144, 165)) {
  Write-VddSettings $Count $Rates
  $tmp = Join-Path $env:RUNNER_TEMP 'vdd'
  New-Item -ItemType Directory -Force $tmp | Out-Null

  # Driver-only package from the latest release, and nefcon (driver installer) – both signed
  $rel = Invoke-RestMethod -Headers @{ Authorization = "Bearer $env:GH_TOKEN" } 'https://api.github.com/repos/VirtualDrivers/Virtual-Display-Driver/releases/latest'
  Write-Host "Virtual Display Driver release $($rel.tag_name): $(($rel.assets | ForEach-Object name) -join ', ')"
  $asset = $rel.assets | Where-Object { $_.name -match '^VirtualDisplayDriver' -and $_.name -match 'Driver\.Only' -and $_.name -match 'x86|x64|amd64' -and $_.name -notmatch 'arm' } | Select-Object -First 1
  if (-not $asset) { throw 'No driver-only x64 package found in the latest release' }
  Invoke-WebRequest $asset.browser_download_url -OutFile "$tmp\driver.zip" -UseBasicParsing
  Expand-Archive "$tmp\driver.zip" "$tmp\driver" -Force
  Invoke-WebRequest 'https://github.com/nefarius/nefcon/releases/download/v1.14.0/nefcon_v1.14.0.zip' -OutFile "$tmp\nefcon.zip" -UseBasicParsing
  Expand-Archive "$tmp\nefcon.zip" "$tmp\nefcon" -Force

  Get-ChildItem "$tmp\driver" -Recurse | ForEach-Object { Write-Host "  $($_.FullName.Substring($tmp.Length)) $($_.Length)" }
  # Some releases wrap the driver in a second zip
  Get-ChildItem "$tmp\driver" -Recurse -Filter '*.zip' | ForEach-Object { Expand-Archive $_.FullName (Join-Path $_.DirectoryName $_.BaseName) -Force }
  $inf = Get-ChildItem "$tmp\driver" -Recurse -Filter '*.inf' | Where-Object { $_.Name -match 'vdd' } | Select-Object -First 1
  if (-not $inf) { $inf = Get-ChildItem "$tmp\driver" -Recurse -Filter '*.inf' | Select-Object -First 1 }
  if (-not $inf) { throw 'No .inf file in the driver package' }
  $cat = Get-ChildItem $inf.DirectoryName -Filter '*.cat' | Select-Object -First 1
  Write-Host "Driver files: $($inf.FullName)"

  # Trust the driver's publisher certificate so Windows installs it without a prompt
  $certs = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2Collection
  $certs.Import([IO.File]::ReadAllBytes($cat.FullName))
  foreach ($c in $certs) {
    $f = Join-Path $tmp "$($c.Thumbprint).cer"
    [IO.File]::WriteAllBytes($f, $c.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    Import-Certificate -FilePath $f -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
  }

  $nefcon = Get-ChildItem "$tmp\nefcon" -Recurse -Filter 'nefconc.exe' | Where-Object { $_.DirectoryName -match 'x64' } | Select-Object -First 1
  & $nefcon.FullName --create-device-node --hardware-id 'Root\MttVDD' --class-name Display --class-guid 4d36e968-e325-11ce-bfc1-08002be10318 | Out-Host
  Write-Host "create-device-node exit $LASTEXITCODE"
  & $nefcon.FullName --install-driver --inf-path $inf.FullName | Out-Host
  Write-Host "install-driver exit $LASTEXITCODE"
  Wait-VirtualDisplays $Count
}

function Wait-VirtualDisplays([int]$Count, [int]$Seconds = 30) {
  for ($i = 0; $i -lt $Seconds; $i++) {
    if ((Get-VirtualDisplays).Count -eq $Count) { Start-Sleep 2; return $true }
    Start-Sleep 1
  }
  Write-Host "Expected $Count virtual display(s), found $((Get-VirtualDisplays).Count)"
  return $false
}

# Restart the virtual display device (if the driver stops answering after many quick reloads)
function Restart-VirtualDisplayDevice {
  $dev = Get-PnpDevice -Class Display | Where-Object { $_.FriendlyName -match 'Virtual Display Driver' } | Select-Object -First 1
  if ($dev) { Write-Host "restarting $($dev.InstanceId)"; pnputil /restart-device "$($dev.InstanceId)" | Out-Host }
}

# "Plug in" or "unplug" virtual monitors: the driver reloads with the new count
function Set-VirtualDisplayCount([int]$Count) {
  for ($attempt = 1; $attempt -le 3; $attempt++) {
    try {
      $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'MTTVirtualDisplayPipe', [System.IO.Pipes.PipeDirection]::InOut)
      $pipe.Connect(10000)
      $bytes = [Text.Encoding]::Unicode.GetBytes("SETDISPLAYCOUNT $Count")
      $pipe.Write($bytes, 0, $bytes.Length); $pipe.Flush()
      Start-Sleep 1; $pipe.Dispose()
      if (Wait-VirtualDisplays $Count 30) { return $true }
    } catch { Write-Host "virtual display driver didn't answer (attempt $attempt): $($_.Exception.Message)" }
    # The count is saved in the settings file, so a restarted driver comes back with it
    Write-VddSettings $Count @(60, 120, 144, 165)
    Restart-VirtualDisplayDevice
    if (Wait-VirtualDisplays $Count 30) { return $true }
  }
  return $false
}
