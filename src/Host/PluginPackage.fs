namespace MyDebugTools.Host

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text.Json

[<CLIMutable>]
type PluginExtensionInfo = { PageId: string; Title: string }

[<CLIMutable>]
type PluginManifest = { Id: string; Title: string; Assembly: string; EntryType: string; Version: string; Pages: string array; Extensions: PluginExtensionInfo array }

[<CLIMutable>]
type InstalledPackage = { Id: string; Hash: string; Folder: string; Source: string }

type PackagePreview = { Manifest: PluginManifest; Hash: string; Source: string }

module PluginPackage =
    let private parse stream =
        let m = JsonSerializer.Deserialize<PluginManifest>(stream: Stream)
        if isNull (box m) || [m.Id; m.Title; m.Assembly; m.EntryType] |> List.exists String.IsNullOrWhiteSpace then
            invalidOp "plugin.json 缺少 Id、Title、Assembly 或 EntryType"
        if Path.GetFileName(m.Assembly) <> m.Assembly || not (m.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) then
            invalidOp "Assembly 必须是目录中的 DLL 文件名"
        m

    let manifest folder =
        use stream = File.OpenRead(Path.Combine(folder, "plugin.json"))
        parse stream

    let private inspect path extract =
        use file = File.OpenRead path
        let hash = Convert.ToHexString(SHA256.HashData file)
        file.Position <- 0L
        use archive = new ZipArchive(file, ZipArchiveMode.Read)
        let names = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        for entry in archive.Entries do
            let name = entry.FullName.Replace('\\', '/').TrimEnd('/')
            let parts = name.Split('/')
            if parts |> Array.exists (fun part ->
                String.IsNullOrWhiteSpace part || part.EndsWith(".") || part.EndsWith(" ") ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                Text.RegularExpressions.Regex.IsMatch(part, @"(?i)^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(\.|$)")) then
                invalidOp $"包内路径无效：{entry.FullName}"
            if not (names.Add name) then invalidOp $"包内路径重复：{name}"
            let unixKind = (entry.ExternalAttributes >>> 16) &&& 0xF000
            if entry.ExternalAttributes &&& int FileAttributes.ReparsePoint <> 0 || unixKind = 0xA000 then invalidOp $"包内含链接：{name}"
        let entry name =
            match archive.GetEntry(name) with null -> invalidOp $"包根目录缺少 {name}" | item -> item
        use json = (entry "plugin.json").Open()
        let m = parse json
        entry m.Assembly |> ignore
        match extract with
        | Some (folder, expected) ->
            if expected <> hash then invalidOp "预览后插件包发生变化，请重新选择。"
            for item in archive.Entries do
                let target = Path.GetFullPath(Path.Combine(folder, item.FullName))
                if not (target.StartsWith(folder + string Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) then invalidOp "包内路径越界"
                if item.FullName.EndsWith("/") || item.FullName.EndsWith("\\") then Directory.CreateDirectory(target) |> ignore
                else
                    Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
                    item.ExtractToFile target
        | None -> ()
        { Manifest = m; Hash = hash; Source = Path.GetFullPath path }

    let preview path = inspect path None
    let extract preview folder = inspect preview.Source (Some (folder, preview.Hash)) |> ignore
