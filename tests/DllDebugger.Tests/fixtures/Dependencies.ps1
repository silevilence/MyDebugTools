# Build small native PE images with the local LLVM toolchain. Import libraries
# can be created before their images, which lets the fixture declare a cycle.
param([Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$work = [IO.Directory]::CreateDirectory((Join-Path $Output 'obj')).FullName
function Import-Library($name) {
    $definition = Join-Path $work "$name.def"
    "LIBRARY $name.dll`nEXPORTS`n$name" | Set-Content -LiteralPath $definition
    lld-link /lib /nologo /machine:x64 "/def:$definition" "/out:$work/$name.lib"
}
function Image($name, [string[]]$imports, [string[]]$delay = @(), $directory = $Output) {
    $source = @(
        foreach ($child in $imports) { "__declspec(dllimport) int $child(void);" }
        # The delay helper is a link-time stub. The tests only read these images.
        if ($delay) { 'void* __delayLoadHelper2(void* a, void* b) { return 0; }' }
        "__declspec(dllexport) int ${name}(void) {"
        foreach ($child in $imports) { "$child();" }
        'return 0; }'
    )
    $source | Set-Content -LiteralPath "$work/module.c"
    clang-cl --target=x86_64-pc-windows-msvc /nologo /c /GS- "/Fo$work/module.obj" "$work/module.c"
    $libs = @($imports | ForEach-Object { "$work/$_.lib" })
    $options = @($delay | ForEach-Object { "/delayload:$_.dll" })
    [void][IO.Directory]::CreateDirectory($directory)
    lld-link /dll /noentry /nodefaultlib /noimplib "/out:$directory/$name.dll" "$work/module.obj" @libs @options
}
foreach ($name in 'Root','LeftBranch','Later','SharedLeaf','Choice') { Import-Library $name }
Image 'SharedLeaf' @('Root')
Image 'LeftBranch' @('SharedLeaf')
Image 'Later' @('SharedLeaf')
Image 'Root' @('LeftBranch','Later','Choice') @('Later')
