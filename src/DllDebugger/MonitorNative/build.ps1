param([string]$Arch, [string]$Output)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw '需要 Visual Studio C++ x86/x64 工具和 Windows SDK' }
& "$vs/Common7/Tools/Launch-VsDevShell.ps1" -Arch $Arch -HostArch amd64 -SkipAutomaticLocation
$dir = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Output)))
cl /nologo /std:c++17 /EHsc /MT /O2 /utf-8 "/Fo$dir/capture.obj" "/Fe$Output" "$PSScriptRoot/capture.cpp" /link dbgeng.lib psapi.lib advapi32.lib
exit $LASTEXITCODE
