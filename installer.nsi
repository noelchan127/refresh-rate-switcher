; Refresh Rate Switcher – per-user installer (no admin rights needed)
; Build: makensis installer.nsi   (expects the built app in bin\Release\net48, or set /DBINDIR=path)
;
; Silent install:   RefreshRateSwitcher-Setup-x.y.z.exe /S              (starts with Windows)
;                   RefreshRateSwitcher-Setup-x.y.z.exe /S /NOSTARTUP   (doesn't)

Unicode true
!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!define APPNAME   "Refresh Rate Switcher"
!define APPID     "RefreshRateSwitcher"
!define EXENAME   "RefreshRateSwitcher.exe"
!define VERSION   "1.2.1"
!define PUBLISHER "Noel Chan"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPID}"
!define RUNKEY    "Software\Microsoft\Windows\CurrentVersion\Run"
!ifndef BINDIR
  !define BINDIR "bin\Release\net48"
!endif

Var StartWithWindows   ; 1 = add the startup entry
Var OptionsCheckbox

Name "${APPNAME}"
OutFile "out\RefreshRateSwitcher-Setup-${VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\${APPNAME}"
InstallDirRegKey HKCU "${UNINSTKEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
ManifestDPIAware true

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName"     "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} Setup"
VIAddVersionKey "FileVersion"     "${VERSION}"
VIAddVersionKey "ProductVersion"  "${VERSION}"
VIAddVersionKey "CompanyName"     "${PUBLISHER}"
VIAddVersionKey "LegalCopyright"  "Copyright (C) 2026 ${PUBLISHER}"

!define MUI_ICON   "app.ico"
!define MUI_UNICON "app.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"
!define MUI_FINISHPAGE_RUN_TEXT "Start ${APPNAME} now"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
Page custom OptionsPage OptionsPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ; Default: start with Windows. When upgrading, keep whatever the user chose before.
  StrCpy $StartWithWindows 1
  ReadRegStr $0 HKCU "${UNINSTKEY}" "InstallLocation"
  ${If} $0 != ""
    ReadRegStr $1 HKCU "${RUNKEY}" "${APPID}"
    ${If} $1 == ""
      StrCpy $StartWithWindows 0
    ${EndIf}
  ${EndIf}
  ; Command-line overrides (mainly for silent installs)
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/NOSTARTUP" $R1
  ${IfNot} ${Errors}
    StrCpy $StartWithWindows 0
  ${EndIf}
  ClearErrors
  ${GetOptions} $R0 "/STARTUP" $R1
  ${IfNot} ${Errors}
    StrCpy $StartWithWindows 1
  ${EndIf}
FunctionEnd

Function OptionsPage
  !insertmacro MUI_HEADER_TEXT "Options" "Choose how ${APPNAME} starts."
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateCheckbox} 0 0 100% 12u "&Start ${APPNAME} automatically when I sign in to Windows"
  Pop $OptionsCheckbox
  ${If} $StartWithWindows == 1
    ${NSD_Check} $OptionsCheckbox
  ${EndIf}
  ${NSD_CreateLabel} 12u 16u -12u 36u "Recommended. The app sits in the system tray and switches the refresh rate when you plug in or unplug the charger. You can change this later in the app's settings."
  Pop $0
  nsDialogs::Show
FunctionEnd

Function OptionsPageLeave
  ${NSD_GetState} $OptionsCheckbox $0
  ${If} $0 == ${BST_CHECKED}
    StrCpy $StartWithWindows 1
  ${Else}
    StrCpy $StartWithWindows 0
  ${EndIf}
FunctionEnd

!macro StopApp
  ; Close a running copy so files can be replaced / removed
  nsExec::Exec 'taskkill /F /IM "${EXENAME}"'
  Pop $0
  Sleep 700
!macroend

Section "Install"
  !insertmacro StopApp

  SetOutPath "$INSTDIR"
  File "${BINDIR}\${EXENAME}"
  File "${BINDIR}\${EXENAME}.config"
  File "app.ico"
  File "/oname=LICENSE.txt" "LICENSE"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Start with Windows (per-user Run key). --background = tray only, no window at sign-in.
  ${If} $StartWithWindows == 1
    WriteRegStr HKCU "${RUNKEY}" "${APPID}" '"$INSTDIR\${EXENAME}" --background'
  ${Else}
    DeleteRegValue HKCU "${RUNKEY}" "${APPID}"
  ${EndIf}

  ; Start Menu shortcut (per-user Start Menu)
  CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\${EXENAME}" 0

  ; Settings → Apps → Installed apps entry
  WriteRegStr   HKCU "${UNINSTKEY}" "DisplayName"     "${APPNAME}"
  WriteRegStr   HKCU "${UNINSTKEY}" "DisplayVersion"  "${VERSION}"
  WriteRegStr   HKCU "${UNINSTKEY}" "Publisher"       "${PUBLISHER}"
  WriteRegStr   HKCU "${UNINSTKEY}" "DisplayIcon"     "$INSTDIR\${EXENAME},0"
  WriteRegStr   HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr   HKCU "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" 120
SectionEnd

Section "Uninstall"
  !insertmacro StopApp

  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$INSTDIR\${EXENAME}"
  Delete "$INSTDIR\${EXENAME}.config"
  Delete "$INSTDIR\app.ico"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir  "$INSTDIR"

  ; Startup entry, settings and the Installed-apps entry
  DeleteRegValue HKCU "${RUNKEY}" "${APPID}"
  DeleteRegKey   HKCU "Software\${APPID}"
  DeleteRegKey   HKCU "${UNINSTKEY}"
SectionEnd
