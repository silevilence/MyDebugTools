namespace MyDebugTools.Host

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Runtime.Loader
open System.Text.Json
open System.Threading.Tasks
open MyDebugTools.Contracts

[<CLIMutable>]
type PluginManifest = { Id: string; Title: string; Assembly: string; EntryType: string }

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
            name.Name = "FSharp.Core" || name.Name = "CordiSharp" ||
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
    let retired = ResizeArray<Retired>()
    let mutable collectedSinceCheck = 0
    let options = EnumerationOptions(RecurseSubdirectories = true, IgnoreInaccessible = false,
                                     AttributesToSkip = FileAttributes.ReparsePoint)

    let manifest folder =
        let value = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(folder, "plugin.json")))
        if isNull (box value) || [value.Id; value.Title; value.Assembly; value.EntryType] |> List.exists String.IsNullOrWhiteSpace then
            invalidOp "plugin.json 缺少 Id、Title、Assembly 或 EntryType"
        if Path.GetFileName(value.Assembly) <> value.Assembly || not (value.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) then
            invalidOp "Assembly 必须是目录中的 DLL 文件名"
        value

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

    let savedPaths () =
        if File.Exists settingsFile then JsonSerializer.Deserialize<string array>(File.ReadAllText settingsFile)
        else [||]

    member _.Restore() = [
        for path in savedPaths() do
            try attach path (manifest path)
            with e -> yield $"{path}：{e.Message}"
    ]

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
        match locations.TryGetValue id with
        | true, folder ->
            let! snapshot, info = Task.Run(fun () -> copyOutput folder id)
            let mutable transferred = false
            try
                detach()
                host.Enable(id, fun registry ->
                    transferred <- true
                    activate (snapshot, info) registry)
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
