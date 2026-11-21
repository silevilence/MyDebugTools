param([string]$Output)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
foreach ($arch in 'x86', 'x64') {
    $dir = New-Item -ItemType Directory -Path (Join-Path $Output $arch) -Force
    $target = if ($arch -eq 'x86') { 'i686' } else { 'x86_64' }
    clang-cl "--target=$target-pc-windows-msvc" /c /Z7 /GS- /Od "/Fo$dir/fixture.obj" fixture.c
    $exports = @('/export:forwarded=KERNEL32.GetTickCount', '/export:end_session=KERNEL32.ExitProcess')
    lld-link /dll /noentry /nodefaultlib /debug:full /def:fixture.def @exports "/out:$dir/fixture.dll" "$dir/fixture.obj"
    $entry = if ($arch -eq 'x86') { 'reject_load@12' } else { 'reject_load' }
    lld-link /dll "/entry:$entry" /nodefaultlib /def:fixture.def "/out:$dir/reject.dll" "$dir/fixture.obj"
    clang-cl "--target=$target-pc-windows-msvc" /c /GS- /Od "/Fo$dir/public.obj" fixture.c
    lld-link /dll /noentry /nodefaultlib /debug:full /def:fixture.def "/out:$dir/public.dll" "$dir/public.obj"
}
