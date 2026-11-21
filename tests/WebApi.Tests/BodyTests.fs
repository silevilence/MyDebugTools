module MyDebugTools.WebApi.Tests.BodyTests

open System
open System.IO
open System.Threading
open Microsoft.AspNetCore.Http
open MyDebugTools.WebApi
open Xunit

[<Fact>]
let ``all body modes reach the service with original fields and bytes``() =
    let bytes = [| 0uy; 1uy; 13uy; 128uy; 255uy |]
    let echo (ctx: HttpContext) = task {
        let! seen = task {
            if ctx.Request.HasFormContentType then
                let! form = ctx.Request.ReadFormAsync()
                let output = new MemoryStream()
                use buffer = output
                for part in form.Files do do! part.CopyToAsync(buffer)
                return string form["msg"] + "|" + Convert.ToHexString(buffer.ToArray())
            else
                use buffer = new MemoryStream()
                do! ctx.Request.Body.CopyToAsync(buffer)
                return Convert.ToHexString(buffer.ToArray())
        }
        do! ctx.Response.WriteAsync(string ctx.Request.ContentType + "|" + seen)
    }
    HttpTests.withServer (fun c -> echo c) (fun address -> task {
        let path = Path.GetTempFileName()
        try
            File.WriteAllBytes(path, bytes)
            use client = Http.createClient()
            let send kind media text args = task {
                use req = Http.prepareBody "POST" address args { Kind = kind; MediaType = media; Text = text }
                let! reply = Http.send client req (TimeSpan.FromSeconds 5.) CancellationToken.None
                return reply.Raw
            }
            let! empty = send "空" "" "" [||]
            Assert.Equal("|", empty)
            for media, text in ["application/json", "{\"值\":\"好\"}"; "application/xml", "<值>好</值>"] do
                let! raw = send "原始文本" media text [||]
                Assert.Equal(media + "|" + Convert.ToHexString(Text.Encoding.UTF8.GetBytes(text)), raw)
            let arg = HttpTests.arg "正文" "msg" "中文 +&=%"
            let! form = send "URL 编码表单" "" "" [|arg|]
            Assert.Equal("application/x-www-form-urlencoded|" + arg.Value + "|", form)
            let! parts = send "multipart" "" "" [|arg; HttpTests.arg "文件" "upload" path|]
            Assert.StartsWith("multipart/form-data; boundary=", parts)
            Assert.EndsWith("|" + arg.Value + "|" + Convert.ToHexString(bytes), parts)
            let! file = send "二进制文件" "" path [||]
            Assert.Equal("application/octet-stream|" + Convert.ToHexString(bytes), file)
        finally File.Delete path
    })
