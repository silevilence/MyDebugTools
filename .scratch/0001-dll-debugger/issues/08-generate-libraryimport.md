# 0001-08 — 生成 LibraryImport 代码

Status: ready-for-agent

**What to build:** 为同一批已确认签名选择 C# LibraryImport，取得可使用的声明与示例。

**Blocked by:** [0001-07 生成 DllImport 代码](07-generate-dllimport.md)

## 验收条件

- [ ] 单函数示例和多选声明均可从工作台复制或导出。
- [ ] 对受支持的签名编译生成物并运行，返回值和输出参数与同一 DLL 的已知结果一致。
- [ ] 混选可表达与不可表达的签名时，结果按函数列明限制，受支持部分仍能独立编译运行。
