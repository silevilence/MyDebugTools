module MyDebugTools.Tests.Monitoring

open System
open System.Threading.Tasks
open MyDebugTools.Contracts
open MyDebugTools.Host
open Xunit

[<Fact>]
let ``a collecting plugin stays active until stop has finished`` () = task {
    use host = new Host()
    let mutable target = Some "sample.exe (42)"
    let mutable released = false
    let finish = TaskCompletionSource()
    host.Add {
        Id = "monitor"; Title = ""; Core = false
        Create = fun () ->
            { new IDesktopPlugin with
                member _.Activate reg =
                    reg.Monitor { Target = fun () -> target
                                  Stop = fun () -> task {
                                      do! finish.Task
                                      target <- None
                                  } }
                    reg.Own { new IDisposable with member _.Dispose() = released <- true } }
    }
    host.Enable "monitor"
    let session = (host.Monitor "monitor").Value
    Assert.Equal(Some "sample.exe (42)", session.Target())
    Assert.Throws<InvalidOperationException>(fun () -> host.Disable "monitor") |> ignore
    let stopping = session.Stop()
    Assert.False released
    Assert.True(host.Enabled "monitor")
    finish.SetResult()
    do! stopping
    host.Disable "monitor"
    Assert.True released
}
