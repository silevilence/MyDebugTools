namespace MyDebugTools.WebApi

open System
open System.Diagnostics
open System.Net.Http
open System.Text.RegularExpressions
open System.Threading

type Argument = { Name: string; Location: string; Value: string }

type Reply = {
    Summary: string
    Status: int option
    ElapsedMs: int64
    Headers: string
    MediaType: string
    Raw: string
}

module Http =
    let createClient () =
        new HttpClient(new HttpClientHandler(AllowAutoRedirect = false, UseCookies = false),
                       Timeout = Timeout.InfiniteTimeSpan)

    let prepare (methodName: string) (url: string) (args: Argument array) =
        let verb = HttpMethod(methodName.Trim())
        let mutable address = url.Trim()
        let query = ResizeArray<string>()
        for arg in args do
            match arg.Location with
            | "路径" ->
                let key = "{" + arg.Name + "}"
                if not (address.Contains key) then invalidArg "URL" $"URL 中没有路径占位符 {key}"
                address <- address.Replace(key, Uri.EscapeDataString arg.Value)
            | "查询" -> query.Add(Uri.EscapeDataString arg.Name + "=" + Uri.EscapeDataString arg.Value)
            | _ -> ()
        if Regex.IsMatch(address, @"\{[^{}]+\}") then invalidArg "URL" "URL 中仍有未填写的占位符"
        let uri = Uri(address, UriKind.Absolute)
        if uri.Scheme <> "http" && uri.Scheme <> "https" then invalidArg "URL" "请输入 HTTP 或 HTTPS 地址"
        let target = UriBuilder(uri, Fragment = "")
        target.Query <- String.concat "&" (seq {
            let existing = uri.Query.TrimStart('?')
            if existing <> "" then yield existing
            yield! query
        })
        let request = new HttpRequestMessage(verb, target.Uri)
        try
            for arg in args do
                if arg.Location = "请求头" then request.Headers.Add(arg.Name, arg.Value)
            request
        with _ ->
            request.Dispose()
            reraise()

    let send (client: HttpClient) (request: HttpRequestMessage) (timeout: TimeSpan) (cancel: CancellationToken) = task {
        use deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel)
        deadline.CancelAfter timeout
        let watch = Stopwatch.StartNew()
        let failure summary = {
            Summary = summary; ElapsedMs = watch.ElapsedMilliseconds; Status = None
            Headers = ""; MediaType = ""; Raw = ""
        }
        try
            use! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
            let! raw = response.Content.ReadAsStringAsync(deadline.Token)
            let code = int response.StatusCode
            return {
                Summary = $"HTTP {code} {response.ReasonPhrase}".TrimEnd()
                Status = Some code; ElapsedMs = watch.ElapsedMilliseconds
                Headers = string response.Headers + string response.Content.Headers + string response.TrailingHeaders
                MediaType = match response.Content.Headers.ContentType with null -> "" | t -> t.MediaType
                Raw = raw
            }
        with
        | :? OperationCanceledException when cancel.IsCancellationRequested -> return failure "已取消"
        | :? OperationCanceledException when deadline.IsCancellationRequested -> return failure "请求超时"
        | :? HttpRequestException as e -> return failure $"连接或传输失败 · {e.Message}"
        | e -> return failure $"发送失败 · {e.Message}"
    }
