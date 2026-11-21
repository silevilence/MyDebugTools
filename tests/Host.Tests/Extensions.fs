module MyDebugTools.Tests.Extensions

open Xunit
open MyDebugTools.Host
open MyDebugTools.Samples

[<Fact>]
let ``consumer waits for provider and mounts once on each activation`` () =
    use host = new Host()
    host.Add { Id = "actions"; Title = ""; Core = false; Create = fun () -> WorkbenchActions() }
    host.Add { Id = "provider"; Title = ""; Core = false; Create = fun () -> Workbench() }
    host.Enable "actions"
    for _ in 1..2 do
        Assert.Empty(host.Actions "sample.workbench")
        host.Enable "provider"
        let _, invoke = Assert.Single(host.Actions "sample.workbench")
        Assert.Equal("当前选中：请求 A", invoke())
        host.Disable "provider"
        Assert.True(host.Enabled "actions")
