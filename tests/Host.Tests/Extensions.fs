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
    let tab = { PageId = "dll.debugger"; PointId = "tabs"; MinVersion = 1; MaxVersion = 1
                Title = "依赖"; Create = fun () -> failwith "only create inside the workbench" }
    host.Add { Id = "deps"; Title = "依赖"; Core = false
               Create = fun () -> { new IDesktopPlugin with member _.Activate r = r.DllTab tab } }
    let mutable version = 1
    host.Add { Id = "provider"; Title = ""; Core = false
               Create = fun () ->
                   { new IDesktopPlugin with
                       member _.Activate r =
                           r.Page { Id = tab.PageId; Title = ""; Create = fun () -> failwith "UI only" }
                           r.DllTabPoint { PageId = tab.PageId; PointId = tab.PointId; Version = version } } }
    let mounted () = host.DllTabs(tab.PageId, tab.PointId)
    host.Enable "provider"
    let mutable changes = 0
    use subscription = host.DllTabsChanged.Subscribe(fun () -> changes <- changes + 1)
    for _ in 1..2 do
        host.Enable "deps"
        host.Enable "deps"
        Assert.Same(tab, mounted() |> List.exactlyOne)
        host.Disable "deps"
        Assert.Empty(mounted())
    Assert.Equal(4, changes)
    host.Enable "deps"
    host.Disable "provider"
    Assert.Empty(mounted())
    Assert.True(host.Enabled "deps")
    version <- 2
    host.Enable "provider"
    Assert.Empty(mounted())
    Assert.Equal("依赖 · dll.debugger/tabs：要求版本 1，实际版本 2", Assert.Single(host.Mismatches "deps"))
    host.Disable "provider"
    version <- 1
    host.Enable "provider"
    Assert.Same(tab, Assert.Single(mounted()))
    Assert.Empty(host.Mismatches "deps")
