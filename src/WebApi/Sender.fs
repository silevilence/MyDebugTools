namespace MyDebugTools.WebApi

open System
open System.Threading
open System.Threading.Tasks

/// One editor sends one request at a time. Finish its log on a worker so closing
/// the window can wait for cancellation without depending on the UI dispatcher.
type Sender(dir) =
    let client = Http.createClient()
    let lifetime = new CancellationTokenSource()
    let mutable pending = Task.CompletedTask

    member _.Send(methodName, url, args, body, source, timeout, cancel: CancellationToken) =
        let work () = task {
            use stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancel)
            use request = Http.prepareBody methodName url args body
            let entry = History.snapshot source request body args
            History.start dir History.limit entry
            let! reply = Http.send client request timeout stop.Token
            History.finish dir entry.Id $"{reply.Summary} · {reply.ElapsedMs} ms"
            return reply
        }
        let result = Task.Run<Reply>(Func<Task<Reply>>(work))
        pending <- result
        result

    interface IDisposable with
        member _.Dispose() =
            lifetime.Cancel()
            // The editor observes send errors. A failed request must still release the client.
            pending.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing).GetAwaiter().GetResult()
            client.Dispose()
            lifetime.Dispose()
