# MyDebugTools 插件开发

编译目标为 net10.0。引用宿主提供的 Contracts 程序集，实现带公开无参构造函数的 IDesktopPlugin。在 Activate 中声明页面、菜单、DLL 页签和可释放资源。文末是本次构建使用的完整 F# 注册契约。

在输出目录根部保存 plugin.json，例如

```json
{"Id":"thirdparty.tools","Title":"第三方工具","Assembly":"ThirdPartyTools.dll","EntryType":"ThirdParty.Tools","Version":"1.0"}
```

Assembly 是目录内的 DLL 文件名。将私有依赖放在输出中。Version、Pages 和 Extensions 可选。Page.Create 按需创建控件。菜单操作接收提供方当前选中的字符串，DLL 页签通过 OpenFile 接收文件路径，通过 Close 结束本次文件会话。消费方用 PageId、PointId 以及包含两端的版本区间指明目标。CurrentTarget() 返回当前调试目标或 None。用 Own 登记订阅、后台任务的停止句柄等资源。正在采集的插件通过 Monitor 声明目标和停止操作。

例如，下列插件在示例工作台的 selection 菜单扩展点显示当前选中项。

```fsharp
namespace ThirdParty
open MyDebugTools.Contracts

type Tools() =
    interface IDesktopPlugin with
        member _.Activate registry =
            registry.MenuAction {
                PageId = "sample.workbench"; PointId = "selection"
                MinVersion = 1; MaxVersion = 1; Title = "查看选中项"
                Invoke = fun selection -> selection
            }
```

在本机客户端调用 install_temporary，传入 {"directory":"C:\\Dev\\ThirdPartyTools\\bin\\Debug\\net10.0"}。在桌面核对插件 ID 和目录后确认加载。覆盖编译后用同一目录再次调用并确认。调用 uninstall_temporary，传入 {"id":"thirdparty.tools"}，即可恢复正式配置并保留开发目录。
