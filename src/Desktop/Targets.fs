module MyDebugTools.Desktop.Targets

open System
open System.Diagnostics
open System.IO
open System.Reflection.PortableExecutable
open System.Runtime.InteropServices
open Microsoft.Win32

type Architecture = X86 | X64

let private architecture = function
    | Machine.I386 -> X86
    | Machine.Amd64 -> X64
    | machine -> invalidOp $"不支持的目标架构：{machine}"

let forFile path =
    use stream = File.OpenRead path
    use image = new PEReader(stream)
    architecture image.PEHeaders.CoffHeader.Machine

[<DllImport("kernel32.dll", SetLastError = true)>]
extern bool private IsWow64Process2(nativeint handle, uint16& processMachine, uint16& nativeMachine)

let forProcess pid =
    use proc = Process.GetProcessById pid
    let mutable processMachine = 0us
    let mutable nativeMachine = 0us
    if not (IsWow64Process2(proc.Handle, &processMachine, &nativeMachine)) then
        raise (ComponentModel.Win32Exception(Marshal.GetLastWin32Error()))
    let machine = if processMachine = 0us then nativeMachine else processMachine
    architecture (LanguagePrimitives.EnumOfValue machine)

let environment arch =
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
    if available then $"目标 {name} · {executable}"
    else $"目标 {name} · 缺少 .NET 10 {name} 运行时"
