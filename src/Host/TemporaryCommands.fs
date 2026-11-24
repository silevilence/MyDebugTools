namespace MyDebugTools.Host

open System
open System.Threading
open System.Threading.Tasks
open ModelContextProtocol

type TemporaryCommands(catalog: LocalPlugins, onDesktop: (unit -> Task<string>) -> Task<string>,
                       approve, allowStop, refresh) =
    let gate = new SemaphoreSlim(1, 1)

    let run (token: CancellationToken) work = task {
        do! gate.WaitAsync(token)
        try
            try
                return! onDesktop (fun () ->
                    token.ThrowIfCancellationRequested()
                    work())
            with
            | :? OperationCanceledException as e -> return raise e
            | e -> return raise (McpException(e.Message))
        finally gate.Release() |> ignore
    }

    let checkedStop (token: CancellationToken) id = task {
        token.ThrowIfCancellationRequested()
        let! proceed = allowStop id
        token.ThrowIfCancellationRequested()
        return proceed
    }

    member _.Install(directory: string, cancellationToken: CancellationToken) =
        run cancellationToken (fun () -> catalog.InstallTemporary(directory, approve, checkedStop cancellationToken, refresh))

    member _.Uninstall(id: string, cancellationToken: CancellationToken) =
        run cancellationToken (fun () -> catalog.UninstallTemporary(id, checkedStop cancellationToken, refresh))

    member this.Tools = [
        DeveloperTools.methodTool "install_temporary" "复制本机编译输出并请桌面用户确认本次临时加载" this (nameof this.Install)
        DeveloperTools.methodTool "uninstall_temporary" "卸载临时插件并恢复正式版原有状态" this (nameof this.Uninstall)
    ]
