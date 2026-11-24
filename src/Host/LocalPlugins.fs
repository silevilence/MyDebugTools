namespace MyDebugTools.Host

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Runtime.Loader
open System.Text.Json
open System.Threading.Tasks
open MyDebugTools.Contracts

type private PluginContext(entry: string) =
    inherit AssemblyLoadContext(Path.GetFileNameWithoutExtension entry, true)
    let resolver = AssemblyDependencyResolver(entry)
    let folder = Path.GetDirectoryName entry

    member this.ReadAssembly path =
        use stream = File.OpenRead path
        this.LoadFromStream stream

    override this.Load name =
        let shared =
            name.Name = typeof<IDesktopPlugin>.Assembly.GetName().Name ||
            name.Name = "MyDebugTools.Host" || name.Name = "DllDebugger" || name.Name = "FSharp.Core" || name.Name = "CordiSharp" ||
            name.Name = "Avalonia" || name.Name.StartsWith("Avalonia.", StringComparison.Ordinal)
        if shared then AssemblyLoadContext.Default.LoadFromAssemblyName name
        else
            let resolved = resolver.ResolveAssemblyToPath name
            let path = if isNull resolved then Path.Combine(folder, name.Name + ".dll") else resolved
            if File.Exists path then this.ReadAssembly path else null

    override this.LoadUnmanagedDll name =
        match resolver.ResolveUnmanagedDllToPath name with
        | null -> 0n
        | path -> this.LoadUnmanagedDllFromPath path

type private Retired = { Id: string; Context: WeakReference; Snapshot: DirectoryInfo }

type LocalPlugins(host: Host, settingsFile: string) =
    let locations = Dictionary<string, string>()
    let bundled = HashSet<string>()
    let manifests = Dictionary<string, PluginManifest>()
    let retired = ResizeArray<Retired>()
    let mutable collectedSinceCheck = 0
    let options = EnumerationOptions(RecurseSubdirectories = true, IgnoreInaccessible = false,
                                     AttributesToSkip = FileAttributes.ReparsePoint)

    let manifest folder = PluginPackage.manifest folder
    let packageList = Path.ChangeExtension(settingsFile, ".packages.json")
    let packageRoot = Path.Combine(Path.GetDirectoryName(settingsFile), "packages")
    let readPackages () =
        if File.Exists packageList then JsonSerializer.Deserialize<InstalledPackage array>(File.ReadAllText packageList)
        else [||]
    let packageFolder (item: InstalledPackage) =
        if not (Guid.TryParseExact(item.Folder, "N") |> fst) then invalidOp "受管包目录标识无效"
        Path.Combine(packageRoot, item.Folder)
    let savePackages (items: InstalledPackage array) =
        let temp = packageList + "." + Guid.NewGuid().ToString("N")
        try
            File.WriteAllText(temp, JsonSerializer.Serialize items)
            File.Move(temp, packageList, true)
        finally
            if File.Exists temp then File.Delete temp
    let lockCatalog () =
        Directory.CreateDirectory(Path.GetDirectoryName settingsFile) |> ignore
        File.Open(settingsFile + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None)
    let packages = Dictionary<string, InstalledPackage>()

    let sweep () =
        for item in retired.ToArray() do
            if not item.Context.IsAlive then
                // Snapshot is exclusively created by CreateTempSubdirectory below.
                try
                    item.Snapshot.Delete(true)
                    retired.Remove item |> ignore
                    collectedSinceCheck <- collectedSinceCheck + 1
                with :? IOException -> ()

    let copyOutput folder id =
        let info = manifest folder
        if info.Id <> id then invalidOp "目录中的插件 Id 已改变"
        let snapshot = Directory.CreateTempSubdirectory("MyDebugTools-plugin-")
        try
            let relativeSnapshot = Path.GetRelativePath(folder, snapshot.FullName)
            if not (Path.IsPathRooted relativeSnapshot || relativeSnapshot.StartsWith(".." + string Path.DirectorySeparatorChar, StringComparison.Ordinal)) then
                invalidOp "请选择插件的独立编译输出目录"
            for file in Directory.EnumerateFiles(folder, "*", options) do
                let target = Path.Combine(snapshot.FullName, Path.GetRelativePath(folder, file))
                Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
                use source = File.OpenRead file
                use destination = File.Create target
                source.CopyTo destination
            snapshot, info
        with _ ->
            snapshot.Delete(true)
            reraise()

    let activate (snapshot: DirectoryInfo, info: PluginManifest) (registry: Registration) =
        sweep()
        let mutable context: PluginContext option = None
        let retire () =
            match context with
            | Some alc ->
                retired.Add { Id = info.Id; Context = WeakReference(alc, true); Snapshot = snapshot }
                alc.Unload()
                context <- None
            | None -> snapshot.Delete(true)
        registry.Own { new IDisposable with member _.Dispose() = retire() }
        let entry = Path.Combine(snapshot.FullName, info.Assembly)
        let alc = PluginContext(entry)
        context <- Some alc
        let assembly = alc.ReadAssembly entry
        let plugin = Activator.CreateInstance(assembly.GetType(info.EntryType, true)) :?> IDesktopPlugin
        plugin.Activate registry

    let attach folder (info: PluginManifest) =
        host.Add {
            Id = info.Id; Title = info.Title; Core = false
            Create = fun () -> { new IDesktopPlugin with member _.Activate reg = activate (copyOutput folder info.Id) reg }
        }
        locations.Add(info.Id, folder)
        manifests[info.Id] <- info

    let savedPaths () =
        if File.Exists settingsFile then JsonSerializer.Deserialize<string array>(File.ReadAllText settingsFile)
        else [||]

    member _.AddBundled(folder) =
        let info = manifest folder
        attach folder info
        bundled.Add info.Id |> ignore

    member _.Source(id) =
        if packages.ContainsKey id then "外部 ZIP"
        elif locations.ContainsKey id && not (bundled.Contains id) then "外部开发目录"
        else "随附普通"

    member _.Version(id) =
        match manifests.TryGetValue id with
        | true, info when not (String.IsNullOrWhiteSpace info.Version) -> info.Version
        | _ -> "未知"

    member _.Restore() = [
        for item in readPackages() do
            try
                let folder = packageFolder item
                if item.PendingRemoval then
                    host.Add { Id = item.Id; Title = item.Id; Core = false; Create = fun () -> invalidOp "请先完成卸载" }
                    locations.Add(item.Id, folder)
                else
                    let m = manifest folder
                    if m.Id <> item.Id then invalidOp "安装包标识与登记不符"
                    attach folder m
                packages.Add(item.Id, item)
            with e -> yield $"{item.Id}：{e.Message}"
        for path in savedPaths() do
            try attach path (manifest path)
            with e -> yield $"{path}：{e.Message}"
    ]

    member _.Package id =
        match packages.TryGetValue id with true, item -> Some item | _ -> None

    member _.RemoveExternal(id, detached: unit -> unit) =
        use guard = lockCatalog()
        if bundled.Contains id || not (locations.ContainsKey id) then invalidOp "只能移除外部插件"
        host.Disable id
        detached()
        match packages.TryGetValue id with
        | true, item ->
            let items = readPackages()
            if not (items |> Array.contains item) then invalidOp "安装记录已变化。"
            let pending = { item with PendingRemoval = true }
            savePackages (items |> Array.map (fun x -> if x.Id = id then pending else x))
            packages[id] <- pending
            let folder = packageFolder item
            if Directory.Exists folder then Directory.Delete(folder, true)
            savePackages (items |> Array.filter (fun x -> x.Id <> id))
            packages.Remove id |> ignore
        | _ ->
            let folder = locations[id]
            let saved = savedPaths() |> Array.filter (fun path -> not (String.Equals(path, folder, StringComparison.OrdinalIgnoreCase)))
            let temp = settingsFile + "." + Guid.NewGuid().ToString("N")
            try
                File.WriteAllText(temp, JsonSerializer.Serialize saved)
                File.Move(temp, settingsFile, true)
            finally
                if File.Exists temp then File.Delete temp
        host.Remove id
        locations.Remove id |> ignore
        manifests.Remove id |> ignore

    member _.Replace(preview: PackagePreview, detached: unit -> unit) =
        use guard = lockCatalog()
        let items = readPackages()
        let id = preview.Manifest.Id
        let old = packages[id]
        if old.PendingRemoval then invalidOp "旧包正在卸载，请先重试卸载。"
        if not (items |> Array.contains old) then invalidOp "安装记录已变化，请重新打开管理页。"
        let previousFolder = packageFolder old
        let previousManifest = manifests[id]
        let enabled = host.Enabled id
        let updated = { old with Hash = preview.Hash; Folder = Guid.NewGuid().ToString("N"); Source = preview.Source }
        let folder = packageFolder updated
        Directory.CreateDirectory folder |> ignore
        let mutable stage = "校验并解包"
        let mutable removed = false
        let mutable attached = false
        try
            PluginPackage.extract preview folder
            stage <- "撤下旧版"
            host.Remove id
            removed <- true
            detached()
            locations.Remove id |> ignore
            stage <- "注册新版"
            attach folder preview.Manifest
            attached <- true
            stage <- "启用新版"
            host.Enable id
            stage <- "保存安装记录"
            savePackages (items |> Array.map (fun item -> if item.Id = id then updated else item))
            packages[id] <- updated
        with error ->
            if attached then host.Remove id
            if removed then
                locations.Remove id |> ignore
                attach previousFolder previousManifest
                if enabled then host.Enable id
            Directory.Delete(folder, true)
            raise (InvalidOperationException($"{stage}失败 · {error.Message}", error))
        Directory.Delete(previousFolder, true)

    member this.Install(preview: PackagePreview) =
        use guard = lockCatalog()
        let items = readPackages()
        let id = preview.Manifest.Id
        match items |> Array.tryFind (fun item -> item.Id = id) with
        | Some item when item.Hash = preview.Hash && packages.ContainsKey id -> ()
        | Some _ -> invalidOp "同 ID 的包已安装。请确认替换版本。"
        | None ->
            if host.Plugins |> List.exists (fun p -> p.Id = id) then invalidOp $"与现有插件 ID 冲突：{id}"
            let item = { Id = id; Folder = Guid.NewGuid().ToString("N"); Hash = preview.Hash; Source = preview.Source; PendingRemoval = false }
            let folder = packageFolder item
            Directory.CreateDirectory folder |> ignore
            let mutable stage = "校验并解包"
            let mutable attached = false
            try
                PluginPackage.extract preview folder
                stage <- "注册插件"
                attach folder preview.Manifest
                attached <- true
                stage <- "启用插件"
                host.Enable id
                stage <- "保存安装记录"
                savePackages (Array.append items [|item|])
                packages.Add(id, item)
            with error ->
                if attached then
                    host.Remove id
                    locations.Remove id |> ignore
                    manifests.Remove id |> ignore
                Directory.Delete(folder, true)
                raise (InvalidOperationException($"{stage}失败 · {error.Message}", error))

    member _.Relations id =
        match host.Relations id, manifests.TryGetValue id with
        | Some actual, _ -> Some actual
        | _, (true, m) when not (isNull m.Pages && isNull m.Extensions) ->
            Some ((Option.ofObj m.Pages |> Option.defaultValue [||] |> Array.toList),
                  [for e in Option.ofObj m.Extensions |> Option.defaultValue [||] do
                      if not (isNull (box e)) && not (String.IsNullOrWhiteSpace e.PageId) then yield e.PageId, e.Title])
        | _ -> None


    member _.AddDirectory(path) =
        let folder = DirectoryInfo(path).FullName
        if locations.ContainsValue folder then invalidOp "此目录已添加"
        let info = manifest folder
        if host.Plugins |> List.exists (fun plugin -> plugin.Id = info.Id) then invalidOp $"插件标识重复：{info.Id}"
        Directory.CreateDirectory(Path.GetDirectoryName settingsFile) |> ignore
        use settingsLock = File.Open(settingsFile + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None)
        let saved = HashSet<string>(savedPaths(), StringComparer.OrdinalIgnoreCase)
        if saved.Add folder then
            let temp = settingsFile + "." + Guid.NewGuid().ToString("N")
            try
                File.WriteAllText(temp, JsonSerializer.Serialize(saved))
                File.Move(temp, settingsFile, true)
            finally
                if File.Exists temp then File.Delete temp
        attach folder info

    // Copy privately on a worker before releasing the current version on the UI
    // thread. Page factories and registration continue to run on the UI thread.
    member _.EnableAsync(id, detach: unit -> unit) = task {
        if packages.ContainsKey id && packages[id].PendingRemoval then invalidOp "请先完成卸载"
        match locations.TryGetValue id with
        | true, folder ->
            let! snapshot, info = Task.Run(fun () -> copyOutput folder id)
            let mutable transferred = false
            try
                detach()
                host.Enable(id, fun registry ->
                    transferred <- true
                    activate (snapshot, info) registry)
                manifests[id] <- info
            finally
                if not transferred then snapshot.Delete(true)
        | _ ->
            detach()
            host.Enable id
    }

    member _.Directory id =
        match locations.TryGetValue id with true, path -> Some path | _ -> None

    /// Collection is explicit so normal navigation never forces a full GC.
    member _.CheckUnloads() =
        GC.Collect()
        GC.WaitForPendingFinalizers()
        GC.Collect()
        sweep()
        let count = collectedSinceCheck
        collectedSinceCheck <- 0
        count, [ for item in retired -> item.Id ]
