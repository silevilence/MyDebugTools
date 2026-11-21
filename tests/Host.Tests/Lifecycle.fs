module MyDebugTools.Tests.Lifecycle

open System
open Xunit
open MyDebugTools.Host
open MyDebugTools.Contracts
open MyDebugTools.Samples

[<Fact>]
let ``fixed management page survives two ordinary plugin lifetimes`` () =
    use host = new Host()
    let mutable disposed = 0
    let manager = { Id = "manager"; Title = "管理"; Create = fun () -> failwith "UI only" }
    host.Add { Id = "manager"; Title = "管理"; Core = true
               Create = fun () -> { new IDesktopPlugin with member _.Activate reg = reg.Page manager } }
    host.Add { Id = "sample"; Title = "工作台"; Core = false
               Create = fun () -> { new IDesktopPlugin with
                                        member _.Activate reg =
                                            (Workbench() :> IDesktopPlugin).Activate reg
                                            reg.Own { new IDisposable with member _.Dispose() = disposed <- disposed + 1 } } }
    for round in 1..2 do
        host.Enable "sample"
        host.Enable "sample"
        Assert.Equal(2, host.Pages.Length)
        host.Disable "sample"
        Assert.Equal(round, disposed)
        Assert.Equal("manager", (Assert.Single host.Pages).Id)
    Assert.Throws<InvalidOperationException>(fun () -> host.Disable "manager") |> ignore
