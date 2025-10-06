; Expects VERSION and BUILDPATH variables passed in
;
; Include Modern UI and required libraries
!include MUI2.nsh
!include DotNetCore.nsh

;-------------------------------- ; Custom Variables

!define PRODUCT "WorldBuilder"
!define ICON "WorldBuilder\Assets\avalonia-logo.ico"
!define COMPANY "xAI"
!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"

;-------------------------------- ; General Information

Name "${PRODUCT}"
OutFile "${PRODUCT}Installer-${VERSION}.exe"
InstallDir "$PROGRAMFILES64${PRODUCT}"
InstallDirRegKey HKLM "Software${PRODUCT}" "Install_Dir"
RequestExecutionLevel admin

;-------------------------------- ; MUI Settings

!define MUI_ABORTWARNING
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP "${NSISDIR}\Contrib\Graphics\Header\nsis.bmp" ; Replace with custom header image if available
!define MUI_WELCOMEFINISHPAGE_BITMAP "${NSISDIR}\Contrib\Graphics\Wizard\win.bmp" ; Replace with custom wizard image if available
!define MUI_WELCOMEPAGE_TITLE "${PRODUCT} Setup"
!define MUI_WELCOMEPAGE_TEXT "Welcome to the ${PRODUCT} Setup Wizard. This will install ${PRODUCT} ${VERSION} on your computer. Click Next to continue."
!define MUI_FINISHPAGE_TITLE "${PRODUCT} Installation Complete"
!define MUI_FINISHPAGE_TEXT "Thank you for installing ${PRODUCT}! The application has been successfully installed. Click Finish to close this wizard."
!define MUI_FINISHPAGE_RUN "$INSTDIR\WorldBuilder.Desktop.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Run ${PRODUCT} now"

;-------------------------------- ; Installer Pages

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

;-------------------------------- ; Uninstaller Pages

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

;-------------------------------- ; Languages

!insertmacro MUI_LANGUAGE "English"

;-------------------------------- ; Installer Components

Section "Main Component (Required)" SEC_MAIN ; Make this section mandatory SectionIn RO

; Write installation path to registry
WriteRegStr HKLM "Software\${PRODUCT}" "Install_Dir" "$INSTDIR"

; Set output path to installation directory
SetOutPath "$INSTDIR"

; Copy main application files
File "${BUILDPATH}\*.*"

; Write uninstaller information to registry
WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "DisplayName" "${PRODUCT}"
WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "QuietUninstallString" "$\"$INSTDIR\uninstall.exe$\" /S"
WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "Publisher" "${COMPANY}"
WriteRegStr   HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "DisplayVersion" "${VERSION}"
WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "NoModify" 1
WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT}" "NoRepair" 1

; Create uninstaller
WriteUninstaller "$INSTDIR\uninstall.exe"

; Check for .NET Core 8.0
!insertmacro CheckDotNetCore 8.0

SectionEnd

Section "Start Menu Shortcuts" SEC_SHORTCUTS ; Create start menu directory
CreateDirectory "$SMPROGRAMS${PRODUCT}"

; Create shortcuts
CreateShortcut "$SMPROGRAMS\${PRODUCT}\Uninstall.lnk" "$INSTDIR\uninstall.exe" "" "$INSTDIR\uninstall.exe" 0
CreateShortcut "$SMPROGRAMS\${PRODUCT}\${PRODUCT}.lnk" "$INSTDIR\WorldBuilder.Desktop.exe" "" "$INSTDIR\WorldBuilder.Desktop.exe" 0

SectionEnd

;-------------------------------- ; Uninstaller

Section "Uninstall" ; Remove registry keys DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall${PRODUCT}" DeleteRegKey HKLM "Software${PRODUCT}"

; Remove installed files
Delete "$INSTDIR\*.*"

; Remove start menu shortcuts
Delete "$SMPROGRAMS\${PRODUCT}\*.*"
RMDir "$SMPROGRAMS\${PRODUCT}"

; Remove installation directory
RMDir /r "$INSTDIR"

SectionEnd

;-------------------------------- ; Descriptions for Components

LangString DESC_SEC_MAIN ${LANG_ENGLISH} "The core components of ${PRODUCT}, required for the application to function."
LangString DESC_SEC_SHORTCUTS ${LANG_ENGLISH} "Optional shortcuts for the Start Menu to easily access ${PRODUCT} and its uninstaller."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
!insertmacro MUI_DESCRIPTION_TEXT ${SEC_MAIN} $(DESC_SEC_MAIN)
!insertmacro MUI_DESCRIPTION_TEXT ${SEC_SHORTCUTS} $(DESC_SEC_SHORTCUTS)
!insertmacro MUI_FUNCTION_DESCRIPTION_END