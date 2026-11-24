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
$framework = (Get-Content -LiteralPath "$publishPath/MyDebugTools.runtimeconfig.json" -Raw | ConvertFrom-Json).runtimeOptions.framework
if ($framework.name -ne 'Microsoft.NETCore.App' -or ([version]$framework.version).Major -ne 10) {
    throw '安装包需要依赖 .NET 10 运行时的桌面宿主'
}
foreach ($component in @{'MyDebugTools.exe'='Amd64'; 'monitor/x86/capture.exe'='I386'; 'monitor/x64/capture.exe'='Amd64'}.GetEnumerator()) {
    $stream = [IO.File]::OpenRead((Join-Path $publishPath $component.Key))
    try {
        $image = [Reflection.PortableExecutable.PEReader]::new($stream)
        try { $machine = $image.PEHeaders.CoffHeader.Machine } finally { $image.Dispose() }
        if ($machine.ToString() -ne $component.Value) { throw "$($component.Key) 架构为 $machine，应为 $($component.Value)" }
    } finally { $stream.Dispose() }
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
$file = Get-Item -LiteralPath $installer
if ($file.VersionInfo.ProductVersion -ne $Version) { throw "安装包版本资源与 $Version 不匹配" }
$file
