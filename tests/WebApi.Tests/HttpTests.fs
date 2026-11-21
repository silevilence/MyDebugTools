module MyDebugTools.WebApi.Tests.HttpTests

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open MyDebugTools.WebApi
open Xunit

let withServer (handle: HttpContext -> Task) check = task {
    let builder = WebApplication.CreateBuilder()
    builder.Logging.ClearProviders() |> ignore
    builder.WebHost.ConfigureKestrel(fun k -> k.Listen(IPAddress.Loopback, 0)) |> ignore
    let app = builder.Build()
    app.Run(RequestDelegate handle)
    do! app.StartAsync()
    try do! check (Seq.head app.Urls)
    finally app.DisposeAsync().AsTask().GetAwaiter().GetResult()
}

let arg location name value = { Name = name; Location = location; Value = value }

[<Fact>]
let ``method encoded arguments and HTTP error body arrive intact``() =
    let echo (ctx: HttpContext) =
        ctx.Response.StatusCode <- 422
        ctx.Response.Headers["X-Seen"] <- "yes"
        ctx.Response.WriteAsync(String.concat "|" [ ctx.Request.Method; ctx.Request.Path.Value
                                                    string ctx.Request.Query["q"]; string ctx.Request.Headers["X-Test"] ])
    withServer echo (fun address -> task {
        use client = Http.createClient()
        for verb in ["GET"; "CHECK"] do
            use req = Http.prepare verb (address + "/{id}?keep=1") [| arg "路径" "id" "a b"; arg "查询" "q" "中 &+"; arg "请求头" "X-Test" "here" |]
            let! reply = Http.send client req (TimeSpan.FromSeconds 5.) CancellationToken.None
            Assert.Equal(Some 422, reply.Status)
            Assert.Contains("X-Seen: yes", reply.Headers)
            Assert.Equal(verb + "|/a b|中 &+|here", reply.Raw)
    })

[<Fact>]
let ``timeout cancellation and connection refusal are distinct``() =
    withServer (fun ctx -> Task.Delay(-1, ctx.RequestAborted)) (fun address -> task {
        use client = Http.createClient()
        use req = Http.prepare "GET" address [||]
        let! timed = Http.send client req (TimeSpan.FromMilliseconds 100.) CancellationToken.None
        Assert.Equal("请求超时", timed.Summary)
        use req2 = Http.prepare "GET" address [||]
        use stop = new CancellationTokenSource(100)
        let! canceled = Http.send client req2 (TimeSpan.FromSeconds 5.) stop.Token
        Assert.Equal("已取消", canceled.Summary)
        use reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        reserved.Bind(IPEndPoint(IPAddress.Loopback, 0))
        use req3 = Http.prepare "GET" $"http://{reserved.LocalEndPoint}" [||]
        let! failed = Http.send client req3 (TimeSpan.FromSeconds 5.) CancellationToken.None
        Assert.StartsWith("连接或传输失败", failed.Summary)
    })
