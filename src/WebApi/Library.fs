namespace MyDebugTools.WebApi

open System
open System.IO
open System.Xml.Linq

type Argument = { Name: string; Location: string; Value: string }

type Parameter = {
    Name: string; Location: string; TypeName: string
    Required: bool; Description: string; DefaultValue: string
}

type Body = { Kind: string; MediaType: string; Text: string }
type Definition = {
    Id: string; Folder: string; Title: string; Method: string; Url: string
    Parameters: Parameter array; Body: Body
}

module Library =
    let emptyBody = { Kind = "空"; MediaType = "text/plain"; Text = "" }
    let blank = {
        Id = ""; Folder = ""; Title = ""; Method = "GET"; Url = ""; Parameters = [||]; Body = emptyBody
    }

    let directory () = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "MyDebugTools", "WebApi")
    let node name (value: string) = XElement(XName.Get name, value)
    let group name (children: XElement seq) = XElement(XName.Get name, Seq.toArray children)
    let children name (parent: XElement) = parent.Elements(XName.Get name)
    let get name (parent: XElement) = parent.Element(XName.Get name).Value
    let load path root =
        if File.Exists path then XElement.Load(path, LoadOptions.PreserveWhitespace)
        else XElement(XName.Get root)

    // Hold the file lock across read/modify/replace so independent desktops keep each other's updates.
    let update path root change =
        Directory.CreateDirectory(Path.GetDirectoryName(path: string)) |> ignore
        use fileLock = File.Open(path + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None)
        let document = load path root
        change document
        let temp = path + ".tmp"
        use output = System.Xml.XmlWriter.Create(temp, System.Xml.XmlWriterSettings(NewLineHandling = System.Xml.NewLineHandling.Entitize))
        document.Save output
        output.Close()
        File.Move(temp, path, true)

    let bodyNode b = group "body" [ node "kind" b.Kind; node "mediaType" b.MediaType; node "text" b.Text ]
    let readBody (parent: XElement) =
        let b = parent.Element(XName.Get "body")
        { Kind = get "kind" b; MediaType = get "mediaType" b; Text = get "text" b }

    let private encode d =
        group "definition" [
            node "id" d.Id; node "folder" d.Folder; node "title" d.Title; node "method" d.Method; node "url" d.Url
            bodyNode d.Body
            for p in d.Parameters do
                group "parameter" [ node "name" p.Name; node "location" p.Location; node "type" p.TypeName
                                    node "required" (string p.Required); node "description" p.Description; node "default" p.DefaultValue ]
        ]

    let private decode el = {
        Id = get "id" el; Folder = get "folder" el; Title = get "title" el
        Method = get "method" el; Url = get "url" el; Body = readBody el
        Parameters = [| for p in children "parameter" el do
                            { Name = get "name" p; Location = get "location" p; TypeName = get "type" p
                              Required = Boolean.Parse(get "required" p); Description = get "description" p
                              DefaultValue = get "default" p } |]
    }

    let private file dir = Path.Combine(dir, "definitions.xml")
    let list dir = load (file dir) "library" |> children "definition" |> Seq.map decode |> Seq.toArray

    let save dir definition =
        if String.IsNullOrWhiteSpace definition.Title then invalidOp "请填写接口名称"
        let saved = if definition.Id = "" then { definition with Id = Guid.NewGuid().ToString("N") } else definition
        update (file dir) "library" (fun document ->
            let old = children "definition" document |> Seq.tryFind (fun el -> get "id" el = saved.Id)
            match old with
            | Some el -> el.ReplaceWith(encode saved)
            | None -> document.Add(encode saved))
        saved
