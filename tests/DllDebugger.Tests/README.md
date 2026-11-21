# DLL 调试验收

此项目在两个真实 Windows .NET 10 进程中运行本地原生样例，并将工作台生成的 C# 和 F# 文本交给各自的编译器。使用 C# 测试项目可以直接构造与 LibraryImport 生成物相同的 C# 类型签名；F# 编译与运行仍通过独立 F# 样例工程验证。

在 Windows 上安装 .NET 10 SDK、对应的 x86/x64 运行时，并将已有的 `clang-cl`、`lld-link` 和 PowerShell 7 放入 PATH。构建目标从 `fixture.c` 和 `fixture.def` 生成两种位数的 DLL 与本地 PDB。再次测试会复用未变化的样例。生成代码的独立编译复用本机 NuGet 缓存。

运行 `dotnet test tests/DllDebugger.Tests -c Release`。发布桌面端后，`tests/Desktop.ps1` 从插件管理页验证实际工作台入口。

如果仅回归本工作台，向 `tests/Desktop.ps1` 传入 `-DllOnly` 和 `-Publish`。复制按钮的验收命令写在 `tests/Clipboard.fsx` 首行，使用内存剪贴板，可与命令行测试并行运行。
