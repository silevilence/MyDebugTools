# 宿主与插件进度

- [x] 需求范围与验收入口已确认，正式规格已发布。
- [x] 4 张工单已分别提交，0000 宿主与插件已完成统一验收。

## 实施工单

- [x] [0000-01 单入口与插件管理](issues/01-desktop-entry.md)
- [x] [0000-02 界面扩展点](issues/02-view-extensions.md)
- [x] [0000-03 本地目录热更新](issues/03-reload-local-plugin.md)
- [x] [0000-04 扩展版本不兼容](issues/04-extension-version-mismatch.md)

## 需求进度

- [x] 应用入口与发布。Windows x64 框架依赖发布版已启动，同一宿主识别 x86、x64 DLL 和进程，并向插件提供当前目标与对应运行时路径。
- [x] 统一界面与扩展声明。核心和普通页面共用注册接口。提供方版本 1 → 2 → 1 时，兼容操作持续可用，失配操作撤下，恢复后只挂载一份。
- [x] 本地插件目录。管理页记住仓库外的编译目录。启用期间可覆盖编译，手动重载后操作使用新版私有依赖。
- [x] 热插拔与私有插件。另一页面的草稿和选中项保留。采集中的插件可以继续监控，或等待停止完成后再停用。真实采集会话按规格由 0002 补验。
- [x] 技术边界。两个插件并存时，各自解析同名私有 DLL。通过旧加载上下文的弱引用检查，确认显示过的 FuncUI 工作台及已执行过的菜单扩展都能回收。

## 统一验收记录

2026-11-21 在 Windows x64、.NET SDK 10.0.301 上执行。

| 验收入口 | 结果 |
| --- | --- |
| `dotnet build -c Release --no-restore` | 0 警告，0 错误。 |
| `dotnet test -c Release --no-restore` | 6 项通过。覆盖两轮启停、提供方恢复、两个私有依赖并存与覆盖编译、版本失配、采集停止顺序、x86/x64 DLL 和进程识别。 |
| `dotnet publish src/Desktop --no-restore -o artifacts/publish` | 生成框架依赖的 win-x64 桌面入口 MyDebugTools.exe。 |
| `pwsh -NoProfile -File tests/Desktop.ps1` | Windows UI 自动化从发布版启停插件、添加仓库外目录并重编译。选择“请求 B”后，菜单结果从 A v1 变为 A v2，草稿原样保留。管理页确认旧第三方插件及两代旧工作台均已回收，版本诊断、恢复后的菜单及 PID 选择均通过。 |

首次还原从 NuGet 补齐本机缺少的 Avalonia.FuncUI 2.0.0 和依赖解析所需的 Avalonia 12.1.0。FSharp.Core 来自本机 SDK，界面实际使用已缓存的 Avalonia 12.1.1，CordiSharp 0.2.0 和测试组件也复用本机缓存。后续构建、样例重编译、测试和发布均复用已还原资源。
