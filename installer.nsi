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
!define VERSION   "1.2.2"
!define PUBLISHER "Noel Chan"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPID}"
!define RUNKEY    "Software\Microsoft\Windows\CurrentVersion\Run"
!define APPROVEDKEY "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"   ; Windows' Startup apps on/off switch
!ifndef BINDIR
  !define BINDIR "bin\Release\net48"
!endif

Var StartWithWindows   ; 1 = add the startup entry
Var OptionsCheckbox
Var WasRunning         ; 1 = the app was running before setup closed it (a silent update starts it again)
Var OldDir             ; where an earlier version was installed

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
  StrCpy $WasRunning 0
  ReadRegStr $0 HKCU "${UNINSTKEY}" "InstallLocation"
  StrCpy $OldDir $0
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

; ---------------------------------------------------------------- closing a running copy
; Other programs that happen to use the same file name are never touched.
;  1. Ask any running copy to exit (1.2.2+ listens for this signal, wherever it was installed) and wait.
;  2. If the installed exe is still in use (1.2.1 or older, or the app is stuck): stop only the process
;     running from that exact path.
;  3. Still in use: ask the user to close it (Retry / Cancel), or fail a silent install with exit code 2.
!define EXITEVENT "Local\RefreshRateSwitcher_Exit"                 ; must match TrayApp.ExitEventName
!define APPMUTEX  "Local\RefreshRateSwitcher_SingleInstance"       ; must match Program.cs

; $R9 = 1 if "$INSTDIR\${EXENAME}" exists and can't be opened for writing (i.e. it's running)
!macro ExeInUse
  StrCpy $R9 0
  ${If} ${FileExists} "$INSTDIR\${EXENAME}"
    ClearErrors
    FileOpen $R8 "$INSTDIR\${EXENAME}" a
    ${If} ${Errors}
      StrCpy $R9 1
    ${Else}
      FileClose $R8
    ${EndIf}
  ${EndIf}
!macroend

; Wait up to 5 seconds for the installed exe to be free
!macro WaitExeFree
  StrCpy $R7 0
  ${Do}
    !insertmacro ExeInUse
    ${If} $R9 == 0
    ${OrIf} $R7 >= 50
      ${Break}
    ${EndIf}
    Sleep 100
    IntOp $R7 $R7 + 1
  ${Loop}
!macroend

; Wait up to 5 seconds for the running copy to finish exiting (it holds this mutex while it runs)
!macro WaitAppExited
  StrCpy $R7 0
  ${Do}
    System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, t "${APPMUTEX}") p .R6'   ; SYNCHRONIZE
    ${If} $R6 P= 0
      ${Break}
    ${EndIf}
    System::Call 'kernel32::CloseHandle(p R6)'
    ${If} $R7 >= 50
      ${Break}
    ${EndIf}
    Sleep 100
    IntOp $R7 $R7 + 1
  ${Loop}
!macroend

!macro StopAppFunction UN
Function ${UN}StopApp
  System::Call 'kernel32::OpenEventW(i 0x0002, i 0, t "${EXITEVENT}") p .R6'   ; EVENT_MODIFY_STATE
  ${If} $R6 P<> 0
    StrCpy $WasRunning 1
    System::Call 'kernel32::SetEvent(p R6)'
    System::Call 'kernel32::CloseHandle(p R6)'
    !insertmacro WaitAppExited
  ${EndIf}

  !insertmacro WaitExeFree
  ${If} $R9 == 1
    StrCpy $WasRunning 1
    ; The path goes through an environment variable so no quoting can break the command
    System::Call 'kernel32::SetEnvironmentVariableW(t "RRS_INSTALLED_EXE", t "$INSTDIR\${EXENAME}")'
    StrCpy $R5 "$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe"   ; 64-bit PowerShell from this 32-bit setup
    ${IfNot} ${FileExists} $R5
      StrCpy $R5 "$SYSDIR\WindowsPowerShell\v1.0\powershell.exe"
    ${EndIf}
    ; Only needed for 1.2.1 and older. A process of the same user needs no -Force, and an inline command needs no execution-policy change
    nsExec::Exec `"$R5" -NoProfile -NonInteractive -Command "Get-CimInstance Win32_Process | Where-Object { $$_.ExecutablePath -eq $$env:RRS_INSTALLED_EXE } | ForEach-Object { Stop-Process -Id $$_.ProcessId }"`
    Pop $R4
    !insertmacro WaitExeFree
  ${EndIf}

  ${DoWhile} $R9 == 1
    ${If} ${Silent}
      SetErrorLevel 2
      DetailPrint "${APPNAME} is still running and couldn't be closed."
      Abort
    ${EndIf}
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "${APPNAME} is still running.$\r$\n$\r$\nClose it from its tray icon (right-click, then Exit), then click Retry." IDRETRY +2
      Abort
    !insertmacro WaitExeFree
  ${Loop}
FunctionEnd
!macroend
!insertmacro StopAppFunction ""
!insertmacro StopAppFunction "un."

Section "Install"
  Call StopApp

  SetOutPath "$INSTDIR"
  File "${BINDIR}\${EXENAME}"
  File "${BINDIR}\${EXENAME}.config"
  File "/oname=LICENSE.txt" "LICENSE"
  Delete "$INSTDIR\app.ico"   ; installed by 1.2.2 and older, not needed (the icon is inside the exe)

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

  ; Installed to a different folder this time: remove the old copy's files (only ours, and the folder if empty)
  ${If} $OldDir != ""
  ${AndIf} $OldDir != $INSTDIR
  ${AndIf} ${FileExists} "$OldDir\${EXENAME}"
    Delete "$OldDir\${EXENAME}"
    Delete "$OldDir\${EXENAME}.config"
    Delete "$OldDir\app.ico"
    Delete "$OldDir\LICENSE.txt"
    Delete "$OldDir\Uninstall.exe"
    RMDir  "$OldDir"
  ${EndIf}

  ; A silent update (e.g. from a script) starts the app again if it was running; an interactive one
  ; offers "Start now" on the last page
  ${If} ${Silent}
  ${AndIf} $WasRunning == 1
    Exec '"$INSTDIR\${EXENAME}" --background'
  ${EndIf}
SectionEnd

Section "Uninstall"
  Call un.StopApp

  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$INSTDIR\${EXENAME}"
  Delete "$INSTDIR\${EXENAME}.config"
  Delete "$INSTDIR\app.ico"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir  "$INSTDIR"

  ; Startup entry, settings and the Installed-apps entry
  DeleteRegValue HKCU "${RUNKEY}" "${APPID}"
  DeleteRegValue HKCU "${APPROVEDKEY}" "${APPID}"
  DeleteRegKey   HKCU "Software\${APPID}"
  DeleteRegKey   HKCU "${UNINSTKEY}"
SectionEnd
