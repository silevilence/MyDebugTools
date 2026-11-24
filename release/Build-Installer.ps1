[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Publish,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$publishPath = (Resolve-Path -LiteralPath $Publish).Path
$required = @('MyDebugTools.exe', 'MyDebugTools.dll', 'MyDebugTools.runtimeconfig.json',
    'DllDebugger.dll', 'DllDebugger.deps.json', 'DllDebugger.runtimeconfig.json',
    'monitor/x86/capture.exe', 'monitor/x64/capture.exe', 'plugins/WebApi/AngleSharp.dll')
foreach ($plugin in 'ApiMonitor', 'DllDependencies', 'WebApi') {
    $required += "plugins/$plugin/plugin.json", "plugins/$plugin/$plugin.dll"
}
foreach ($name in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $publishPath $name) -PathType Leaf)) { throw "发布目录缺少 $name" }
}
$actual = [Reflection.AssemblyName]::GetAssemblyName("$publishPath/MyDebugTools.dll").Version
if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or $actual.ToString(3) -cne $Version -or $actual.Revision -ne 0) {
    throw "宿主版本 $actual 与安装包版本 $Version 不匹配"
}
$compiler = (Get-Command makensis.exe -ErrorAction SilentlyContinue).Source
if (!$compiler) { $compiler = Join-Path ${env:ProgramFiles(x86)} 'NSIS/makensis.exe' }
$output = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
$installer = Join-Path $output "MyDebugTools-$Version-win-x64.exe"
& $compiler /INPUTCHARSET UTF8 /V2 "/DPUBLISH=$publishPath" "/DVERSION=$Version" "/DOUTPUT=$installer" "$PSScriptRoot/installer.nsi"
if ($LASTEXITCODE) { throw "NSIS 生成 $installer 失败 ($LASTEXITCODE)" }
Get-Item -LiteralPath $installer
