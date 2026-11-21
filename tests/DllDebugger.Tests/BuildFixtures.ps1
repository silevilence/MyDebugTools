param([string]$Output)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
foreach ($arch in 'x86', 'x64') {
    $dir = New-Item -ItemType Directory -Path (Join-Path $Output $arch) -Force
    $target = if ($arch -eq 'x86') { 'i686' } else { 'x86_64' }
    & "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe" /nologo "/platform:$arch" "/out:$dir/interactive.exe" 'fixtures\InteractiveTarget.cs'
    clang-cl "--target=$target-pc-windows-msvc" /c /Z7 /GS- /Od "/Fo$dir/fixture.obj" fixture.c
    $exports = @('/export:forwarded=KERNEL32.GetTickCount', '/export:end_session=KERNEL32.ExitProcess')
    lld-link /dll /noentry /nodefaultlib /debug:full /def:fixture.def @exports "/out:$dir/fixture.dll" "$dir/fixture.obj"
    clang-cl "--target=$target-pc-windows-msvc" /c /GS- /Od "/Fo$dir/monitor-target.obj" monitor-target.c
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/Lib'
    $sdk = Get-ChildItem $kits -Directory | Where-Object { Test-Path "$_/um/$arch/kernel32.lib" } | Sort-Object Name -Descending | Select-Object -First 1
    lld-link /subsystem:windows /entry:start /nodefaultlib "/out:$dir/monitor-target.exe" "$dir/monitor-target.obj" "$dir/fixture.lib" "$sdk/um/$arch/kernel32.lib"
    $entry = if ($arch -eq 'x86') { 'reject_load@12' } else { 'reject_load' }
    lld-link /dll "/entry:$entry" /nodefaultlib /def:fixture.def "/out:$dir/reject.dll" "$dir/fixture.obj"
    clang-cl "--target=$target-pc-windows-msvc" /c /GS- /Od "/Fo$dir/public.obj" fixture.c
    lld-link /dll /noentry /nodefaultlib /debug:full /def:fixture.def "/out:$dir/public.dll" "$dir/public.obj"
}
