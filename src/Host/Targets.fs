module MyDebugTools.Host.Targets

open System
open System.Diagnostics
open System.IO
open System.Reflection.PortableExecutable
open System.Runtime.InteropServices
open Microsoft.Win32
open Microsoft.Win32.SafeHandles

open MyDebugTools.Contracts

let private architecture = function
    | Machine.I386 -> X86
    | Machine.Amd64 -> X64
    | machine -> invalidOp $"不支持的目标架构：{machine}"

let forFile path =
    use stream = File.OpenRead path
    use image = new PEReader(stream)
    let headers = image.PEHeaders
    match headers.CoffHeader.Machine, headers.CorHeader with
    | Machine.I386, clr when not (isNull clr) && clr.Flags.HasFlag(CorFlags.ILOnly) && not (clr.Flags.HasFlag(CorFlags.Requires32Bit)) -> X64
    | machine, _ -> architecture machine

[<DllImport("kernel32.dll", SetLastError = true)>]
extern bool private IsWow64Process2(SafeProcessHandle handle, uint16& processMachine, uint16& nativeMachine)

[<DllImport("kernel32.dll", SetLastError = true)>]
extern SafeProcessHandle private OpenProcess(uint32 access, bool inheritHandle, int pid)

let forProcess pid =
    use handle = OpenProcess(0x1000u, false, pid) // PROCESS_QUERY_LIMITED_INFORMATION
    let mutable processMachine = 0us
    let mutable nativeMachine = 0us
    if handle.IsInvalid || not (IsWow64Process2(handle, &processMachine, &nativeMachine)) then
        raise (ComponentModel.Win32Exception(Marshal.GetLastWin32Error()))
    let machine = if processMachine = 0us then nativeMachine else processMachine
    architecture (LanguagePrimitives.EnumOfValue machine)

let inspect source =
    let arch =
        match source with
        | DllFile path -> forFile path
        | ProcessId pid -> forProcess pid
    let name, programFiles =
        match arch with
        | X86 -> "x86", Environment.SpecialFolder.ProgramFilesX86
        | X64 -> "x64", Environment.SpecialFolder.ProgramFiles
    use registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
    use key = registry.OpenSubKey($"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\{name}")
    let fallback = Path.Combine(Environment.GetFolderPath programFiles, "dotnet")
    let root =
        match Option.ofObj key |> Option.map (fun k -> k.GetValue "InstallLocation") with
        | Some (:? string as path) -> path
        | _ -> fallback
    let runtime = Path.Combine(root, "shared", "Microsoft.NETCore.App")
    let executable = Path.Combine(root, "dotnet.exe")
    let available =
        File.Exists executable && Directory.Exists runtime &&
        (Directory.EnumerateDirectories(runtime, "10.*") |> Seq.isEmpty |> not)
    { Source = source; Architecture = arch; DotNetPath = executable }, available

let resolve source =
    let target, available = inspect source
    if not available then invalidOp $"目标 {target.Architecture} · 缺少 .NET 10 {target.Architecture} 运行时"
    target
