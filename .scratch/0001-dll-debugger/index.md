# C ABI DLL 调试进度

- [x] 需求范围与验收入口已确认，正式规格已发布。
- [x] 8 张实施工单均已完成并通过统一验收。

## 实施工单

- [x] [0001-01 打开 DLL 查看导出](issues/01-browse-exports.md)
- [x] [0001-02 录入并复用签名](issues/02-confirm-signature.md)
- [x] [0001-03 跨位数连续调用](issues/03-invoke-across-architectures.md)
- [x] [0001-04 读写复杂参数](issues/04-complex-arguments.md)
- [x] [0001-05 粘贴 C 声明](issues/05-paste-c-declaration.md)
- [x] [0001-06 本地 PDB 补全](issues/06-local-pdb.md)
- [x] [0001-07 生成 DllImport 代码](issues/07-generate-dllimport.md)
- [x] [0001-08 生成 LibraryImport 代码](issues/08-generate-libraryimport.md)

## 需求进度

- [x] 打开与识别
- [x] 手工补全函数签名与字段来源
- [x] 粘贴 C 声明
- [x] 本地 PDB 匹配与补全
- [x] 签名复用
- [x] 选择与调用
- [x] 基础类型、缓冲区、输出参数和普通 C 结构体
- [x] 三种声明、调用示例与批量导出

## 统一验收记录

2026-11-21，Windows x64，.NET SDK 10.0.301。本机 NuGet 缓存提供全部依赖。LLVM 从同一份 C 源码生成 x86 和 x64 样例，后续测试复用样例与运行时。在线下载次数为 0。

| 验收入口 | 实测结果 |
| --- | --- |
| Release 构建及框架依赖发布 | 0 警告、0 错误。桌面入口位于 `artifacts/0001/publish/MyDebugTools.exe`。 |
| `dotnet test MyDebugTools.slnx -c Release --no-build --no-restore` | 22 项通过，其中 DLL 调试 16 项、原宿主 6 项。覆盖按名称及序号浏览和调用、含拒绝初始化入口的 DLL、未知节、签名复用和换版、C 声明位置、本地 PDB 四种匹配情形以及损坏符号。 |
| x86、x64 真实后台进程 | 两种位数均验证了标量、状态计数、句柄、输出指针、UTF-8 字节与结构体字段。结构体大小分别为 12 和 24 字节。超时和进程意外退出后都能重新开始。 |
| 三种生成物 | 为 x86、x64 样例分别生成并编译 C# DllImport、F# DllImport 和 C# LibraryImport，再用匹配位数的运行时执行。核对输出字节、长度及结构体字段。混选时逐项列出跳过原因。 |
| `tests/Desktop.ps1` | 发布版从插件管理进入工作台。x86 仅提供 DLL，通过粘贴 C 声明补齐签名；原路径换入 x64 后要求复核。两次调用 `add(5,7)` 均返回 12。通过系统保存对话框导出的 LibraryImport 文件与预览一致。原宿主桌面回归通过。 |
| `tests/Clipboard.fsx` | 对发布版面板触发真实复制按钮，三种格式分别核对单函数示例及混选批量声明。测试使用进程内的 Avalonia 剪贴板接口，保留用户系统剪贴板内容。 |
