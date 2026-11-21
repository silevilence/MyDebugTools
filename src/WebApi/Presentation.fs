module MyDebugTools.WebApi.Presentation

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Xml
open System.Xml.Linq
open AngleSharp.Dom
open AngleSharp.Html.Parser

let language (mediaType: string) =
    let media = (mediaType.Split(';').[0]).Trim().ToLowerInvariant()
    if media.EndsWith("/json") || media.EndsWith("+json") then ".json"
    elif media = "text/html" || media = "application/xhtml+xml" then ".html"
    elif media.EndsWith("/xml") || media.EndsWith("+xml") then ".xml"
    else ""

type JsonProblem = { Message: string; Offset: int }
let formatJson (text: string) =
    try
        use parsed = JsonDocument.Parse(text)
        use output = new MemoryStream()
        use writer = new Utf8JsonWriter(output, JsonWriterOptions(Indented = true, Encoder = Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping))
        parsed.RootElement.WriteTo(writer)
        writer.Flush()
        Ok (Encoding.UTF8.GetString(output.ToArray()))
    with :? JsonException as e ->
        let lines = text.Split('\n')
        let line = min (lines.Length - 1) (int (e.LineNumber.GetValueOrDefault()))
        let bytes = Encoding.UTF8.GetBytes(lines[line])
        let column = Encoding.UTF8.GetCharCount(bytes.AsSpan(0, min bytes.Length (int (e.BytePositionInLine.GetValueOrDefault()))))
        let start = lines |> Seq.take line |> Seq.sumBy (fun s -> s.Length + 1)
        Error { Message = $"JSON 第 {line + 1} 行，第 {column + 1} 列 · {e.Message}"; Offset = start + column }

let formatXml (text: string) =
    try
        use source = new StringReader(text)
        use reader = XmlReader.Create(source, XmlReaderSettings(DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null))
        Ok (XDocument.Load(reader).ToString())
    with :? XmlException as e -> Error e.Message

// The parser has no browsing context, resource loader or script engine. Translate
// only readable document structure to native text; never mount the supplied HTML.
let htmlBlocks (text: string) =
    let document = HtmlParser().ParseDocument(text)
    let blocks = ResizeArray<string * string>()
    let buffer = StringBuilder()
    let flush tag =
        let value = buffer.ToString()
        buffer.Clear() |> ignore
        let normalized = if tag = "pre" then value else Regex.Replace(value, @"\s+", " ").Trim()
        if normalized <> "" then blocks.Add(tag, normalized)
    let rec walk style (n: INode) =
        if n.NodeType = NodeType.Text then buffer.Append(n.TextContent) |> ignore
        else
            match n.NodeName.ToLowerInvariant() with
            | "script" | "style" | "template" | "iframe" | "object" | "svg" | "noscript" -> ()
            | "br" -> flush style
            | "h1" | "h2" | "h3" | "h4" | "p" | "div" | "li" | "pre" | "tr" as tag ->
                flush style
                for child in n.ChildNodes do walk tag child
                flush tag
            | "td" | "th" ->
                for child in n.ChildNodes do walk style child
                buffer.Append("  ") |> ignore
            | _ -> for child in n.ChildNodes do walk style child
    walk "p" document.Body
    flush "p"
    blocks.ToArray()
