module MyDebugTools.Tests.VersionMismatch

open System.IO
open Xunit
open MyDebugTools.Host
open MyDebugTools.Samples
open MyDebugTools.Tests.Reload

[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private checkVersion (host: Host) version =
    let actions = host.Actions "sample.workbench"
    Assert.Equal("当前选中：请求 A", (Map.ofList actions)["查看选中项"]())
    if version = "2" then
        Assert.Single actions |> ignore
        Assert.Equal("显示摘要 · sample.workbench/summary：要求版本 1，实际版本 2", Assert.Single(host.Mismatches "actions"))
    else
        Assert.Equal(2, actions.Length)
        Assert.Equal("摘要：请求 A", (Map.ofList actions)["显示摘要"]())
        Assert.Empty(host.Mismatches "actions")

[<Fact>]
let ``upgrading one point suspends only its incompatible action`` () =
    let folder = Directory.CreateTempSubdirectory("MyDebugTools-versions-")
    let output = Path.Combine(folder.FullName, "provider")
    try
        use host = new Host()
        let catalog = LocalPlugins(host, Path.Combine(folder.FullName, "settings.json"))
        host.Add { Id = "actions"; Title = ""; Core = false; Create = fun () -> WorkbenchActions() }
        host.Enable "actions"
        for version in ["1"; "2"; "1"] do
            buildSample "Workbench" output ["-p:SummaryVersion=" + version]
            if host.Enabled "local.workbench" then host.Disable "local.workbench"
            else catalog.AddDirectory output
            host.Enable "local.workbench"
            checkVersion host version
        host.Disable "local.workbench"
        let _, pending = catalog.CheckUnloads()
        Assert.Empty pending
    finally
        folder.Delete(true)
