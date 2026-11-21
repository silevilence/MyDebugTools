# 本地插件示例

从仓库根目录运行 `dotnet build samples/ThirdPartyTools -o C:/Temp/MyDebugTools/ThirdPartyTools`。打开插件管理页，选择这个输出目录并启用“第三方工具”。启用“工作台”后，在工作台的“页面操作”中选择“第三方 A”，可以看到当前选中项和私有依赖版本。

保持程序运行，再执行 `dotnet build samples/ThirdPartyTools -o C:/Temp/MyDebugTools/ThirdPartyTools -p:SampleRevision=2`。在管理页点击“重新加载 第三方工具”，然后回到工作台执行同一个操作。草稿会保留，结果中的版本会变成 v2。“检查卸载”会检查旧程序集是否已回收。

私有插件可以在仓库外引用 MyDebugTools.Contracts，使用与宿主一致的 FSharp.Core、Avalonia 和 FuncUI。在编译目录放置本例的 plugin.json，指定唯一的插件 Id、显示名称、入口 DLL 与实现 IDesktopPlugin 的公开无参类型。每个插件使用独立的输出目录。运行中的订阅和后台工作通过 Registration.Own 登记，在 Dispose 完成前结束。采集插件可通过 Registration.Monitor 提供当前目标和异步停止方法。
