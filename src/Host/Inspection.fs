namespace MyDebugTools.Host

open System
open System.Text.Json

type Inspection(host: Host, catalog: LocalPlugins, onDesktop: (unit -> string) -> string) =
    let json value = JsonSerializer.Serialize(value, JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase))
    member _.Plugins() = onDesktop (fun () -> json catalog.Status)
    member _.Interfaces() = onDesktop (fun () ->
        host.Interfaces |> List.map (fun page -> {| Id = page.Id; Description = page.Description |}) |> json)
    member _.Interface(id: string) = onDesktop (fun () ->
        match host.Interfaces |> List.tryFind (fun page -> page.Id = id) with
        | Some page -> json page
        | None -> raise (ModelContextProtocol.McpException($"未知界面：{id}")))
    member _.AllInterfaces() = onDesktop (fun () -> json host.Interfaces)
    member this.Tools = [
        DeveloperTools.tool "list_plugins" "查询正式和临时来源、启用偏好、生效版本和诊断" (Func<string>(this.Plugins))
        DeveloperTools.tool "list_interfaces" "列出当前已注册的界面 ID 和功能说明" (Func<string>(this.Interfaces))
        DeveloperTools.methodTool "get_interface" "按稳定 ID 查询界面与公开扩展点" this (nameof this.Interface)
        DeveloperTools.tool "list_interface_details" "一次读取全部界面及公开扩展点" (Func<string>(this.AllInterfaces))
    ]
