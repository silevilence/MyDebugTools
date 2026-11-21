module MyDebugTools.WebApi.Content

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Collections.Generic
open System.Text
open System.Text.RegularExpressions

let private file path =
    let content = new StreamContent(File.OpenRead path)
    content.Headers.ContentType <- MediaTypeHeaderValue("application/octet-stream")
    content

let renderText (args: Argument array) (text: string) =
    let values = Dictionary<string, string>()
    for a in args do
        if a.Location = "正文" then values[a.Name] <- a.Value
    Regex.Replace(text, @"\{([^{}]+)\}", fun m ->
        match values.TryGetValue m.Groups[1].Value with true, v -> v | _ -> m.Value)

let attach (request: HttpRequestMessage) (body: Body) (args: Argument array) =
    let fields = args |> Array.filter (fun a -> a.Location = "正文" || a.Location = "文件")
    if body.Kind <> "multipart" && fields |> Array.exists (fun a -> a.Location = "文件") then
        invalidOp "文件参数需要 multipart 正文"
    match body.Kind with
    | "空" -> ()
    | "原始文本" ->
        let media = MediaTypeHeaderValue.Parse(body.MediaType)
        let encoding = if String.IsNullOrWhiteSpace media.CharSet then Encoding.UTF8 else Encoding.GetEncoding(media.CharSet.Trim('"'))
        let content = new ByteArrayContent(encoding.GetBytes(renderText args body.Text))
        request.Content <- content
        content.Headers.ContentType <- media
    | "URL 编码表单" ->
        request.Content <- new FormUrlEncodedContent(fields |> Seq.map (fun a -> KeyValuePair(a.Name, a.Value)))
    | "multipart" ->
        let parts = new MultipartFormDataContent()
        request.Content <- parts
        for a in fields do
            let content: HttpContent =
                if a.Location = "文件" then file a.Value
                else new StringContent(a.Value, Encoding.UTF8)
            try
                if a.Location = "文件" then parts.Add(content, a.Name, Path.GetFileName a.Value)
                else parts.Add(content, a.Name)
            with _ ->
                content.Dispose()
                reraise()
    | "二进制文件" -> request.Content <- file body.Text
    | _ -> invalidOp $"未知正文类型 {body.Kind}"
