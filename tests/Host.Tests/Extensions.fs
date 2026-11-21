module MyDebugTools.Tests.Extensions

open Xunit
open MyDebugTools.Host
open MyDebugTools.Samples
open MyDebugTools.Contracts

[<Fact>]
let ``consumer waits for provider and mounts once on each activation`` () =
    use host = new Host()
    host.Add { Id = "actions"; Title = ""; Core = false; Create = fun () -> WorkbenchActions() }
    host.Add { Id = "provider"; Title = ""; Core = false; Create = fun () -> Workbench() }
    host.Enable "actions"
    for _ in 1..2 do
        Assert.Empty(host.Actions "sample.workbench")
        host.Enable "provider"
        let actions = host.Actions "sample.workbench"
        Assert.Equal(2, actions.Length)
        let invoke = (Map.ofList actions)["查看选中项"]
        Assert.Equal("当前选中：请求 A", invoke())
        host.Disable "provider"
        Assert.True(host.Enabled "actions")

[<Fact>]
let ``DLL tab follows its owner's activation`` () =
    use host = new Host()
    let tab = { Title = "依赖"; Create = fun () -> failwith "only create inside the workbench" }
    host.Add { Id = "deps"; Title = "依赖"; Core = false
               Create = fun () -> { new IDesktopPlugin with member _.Activate r = r.DllTab tab } }
    let mutable changes = 0
    use subscription = host.DllTabsChanged.Subscribe(fun () -> changes <- changes + 1)
    for _ in 1..2 do
        host.Enable "deps"
        host.Enable "deps"
        Assert.Same(tab, host.DllTabs |> List.exactlyOne |> snd)
        host.Disable "deps"
        Assert.Empty(host.DllTabs)
    Assert.Equal(4, changes)
