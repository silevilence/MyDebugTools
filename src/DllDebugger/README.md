# DLL 调试内核

桌面入口和插件生命周期在 F# 宿主中。这个程序集提供 PE/PDB 静态读取、签名保存和代码生成，也作为匹配目标位数的 .NET 10 后台程序运行。后台进程保有加载实例和句柄，工作台通过命名管道交换参数与结果。

C# 的 LibraryImport 源生成器要求 C# partial 方法，F# 项目无法直接编译这些生成物。这里把三种输出使用的 ABI 描述和 .NET 互操作类型放在同一个 C# 程序集，桌面层调用同一套接口。F# 可以使用 Reflection.Emit，运行时调用本身没有 C# 语言限制。

## 使用工作台

在插件管理中启用「DLL 工作台」。打开文件后选择导出，或输入名称与 `#序号`。浏览只读文件。核对签名来源，保存后即可填写 JSON 对象调用。无参数函数的签名填 `-`，调用输入填 `{}`。

| 参数行示例 | 调用输入 | 结果 |
| --- | --- | --- |
| `text string in encoding=utf8 owner=caller` | `{"text":"猫"}` | 传入带终止符的 UTF-8 字符串 |
| `dst buffer out length=4 owner=caller` | `{}` | 读回 4 个字节 |
| `n i32* out owner=caller` | `{}` | 读回 32 位整数 |
| `p Point* inout owner=caller` | `{"p":{"x":2,"y":3}}` | 按 `Point pack=8 x:i32 y:i32` 的布局读回字段 |
| `h handle in owner=dll` | 把上次返回的句柄字符串填入 `h` | 使用本次会话的 DLL 句柄 |

「重新开始会话」会释放后台进程。临时字符串、缓冲区和指针参数只在一次调用期间有效。代码导出支持当前函数的调用示例或点选多项导出后的批量声明。示例中调用生成的 `Example()` 即可复现输入，运行项目需和 DLL 位数一致。LibraryImport 项目需启用 `AllowUnsafeBlocks`。
