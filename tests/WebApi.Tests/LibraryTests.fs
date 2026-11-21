module MyDebugTools.WebApi.Tests.LibraryTests

open System
open System.IO
open System.Threading
open Microsoft.AspNetCore.Http
open MyDebugTools.WebApi
open Xunit

[<Fact>]
let ``only explicit saves change the definition and defaults``() =
    HttpTests.withServer (fun ctx -> ctx.Response.WriteAsync(string ctx.Request.Query["id"])) (fun address -> task {
        let dir = Directory.CreateTempSubdirectory()
        try
            let p = { Name = "id"; Location = "查询"; TypeName = "integer"; Required = true; Description = "编号"; DefaultValue = "1001" }
            let header = { p with Name = "X-Test"; Location = "请求头"; DefaultValue = "yes" }
            let d = Library.save dir.FullName {
                Id = ""; Folder = "订单/测试"; Title = "查询"; Method = "POST"; Url = address; Parameters = [|p;header|]
                Body = { Kind = "原始文本"; Text = "a\r\nb"; MediaType = "text/plain" }
            }
            let reopened () = Assert.Single(Library.list dir.FullName)
            Assert.Equal<Definition>(d, reopened())
            use client = Http.createClient()
            for actual in ["1001"; "1002"] do
                use req = Http.prepareBody d.Method d.Url [|HttpTests.arg "查询" "id" actual|] d.Body
                let! reply = Http.send client req (TimeSpan.FromSeconds 5.) CancellationToken.None
                Assert.Equal(actual, reply.Raw)
            use temp = Http.prepare "GET" address [||]
            let! _ = Http.send client temp (TimeSpan.FromSeconds 5.) CancellationToken.None
            Assert.Equal("1001", (reopened()).Parameters[0].DefaultValue)
            Library.save dir.FullName { d with Parameters = [|{ p with DefaultValue = "1003" }|] } |> ignore
            Assert.Equal("1003", (reopened()).Parameters[0].DefaultValue)
        finally dir.Delete(true)
    })
