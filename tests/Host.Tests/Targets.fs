module MyDebugTools.Tests.Targets

open System
open System.Diagnostics
open System.IO
open Xunit
open MyDebugTools.Host
open MyDebugTools.Contracts

[<Fact>]
let ``one x64 host identifies both system DLLs and live process architectures`` () =
    let windows = Environment.GetFolderPath Environment.SpecialFolder.Windows
    let native subfolder = Path.Combine(windows, subfolder, "kernel32.dll")
    Assert.Equal(X64, Targets.forFile(native "System32"))
    Assert.Equal(X86, Targets.forFile(native "SysWOW64"))
    let selected = Targets.resolve(ProcessId Environment.ProcessId)
    Assert.Equal(X64, selected.Architecture)
    Assert.True(File.Exists selected.DotNetPath)
    let start = ProcessStartInfo(Path.Combine(windows, "SysWOW64", "cmd.exe"), "/q",
                                 UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true)
    use child = Process.Start(start)
    try Assert.Equal(X86, Targets.forProcess child.Id)
    finally
        child.StandardInput.Close()
        child.WaitForExit()
