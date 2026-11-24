Unicode true
RequestExecutionLevel user
!include "x64.nsh"
!include "LogicLib.nsh"
!include "MUI2.nsh"

Name "MyDebugTools ${VERSION}"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\MyDebugTools"
SetCompressor zlib
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "MyDebugTools"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "MyDebugTools Windows x64 安装程序"
VIAddVersionKey "LegalCopyright" "MyDebugTools contributors"

!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Function .onInit
  ${IfNot} ${IsNativeAMD64}
    MessageBox MB_OK|MB_ICONSTOP "MyDebugTools 需要 Windows x64。" /SD IDOK
    Abort
  ${EndIf}
  SetRegView 32
  ReadRegStr $0 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64" "InstallLocation"
  ${If} $0 == ""
    StrCpy $0 "$PROGRAMFILES64\dotnet"
  ${EndIf}
  ; NSIS only expands wildcards in the final path component.
  FindFirst $1 $2 "$0\shared\Microsoft.NETCore.App\10.*"
  FindClose $1
  ${If} $2 == ""
    MessageBox MB_OK|MB_ICONEXCLAMATION "启动宿主需要 .NET 10 x64 运行时。执行 x86 DLL 时另需 .NET 10 x86 运行时。" /SD IDOK
  ${EndIf}
FunctionEnd

Section
  SetOutPath "$INSTDIR"
  File /r /x "*.pdb" "${PUBLISH}\*.*"
  CreateShortcut "$SMPROGRAMS\MyDebugTools.lnk" "$INSTDIR\MyDebugTools.exe"
SectionEnd
