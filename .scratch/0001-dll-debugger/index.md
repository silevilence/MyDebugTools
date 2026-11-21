# C ABI DLL 调试进度

- [x] 需求范围与验收入口已确认，正式规格已发布。
- [x] 已确认拆分并建立 8 张工单，功能待实施。

## 实施工单

- [ ] [0001-01 打开 DLL 查看导出](issues/01-browse-exports.md)
- [ ] [0001-02 录入并复用签名](issues/02-confirm-signature.md)
- [ ] [0001-03 跨位数连续调用](issues/03-invoke-across-architectures.md)
- [ ] [0001-04 读写复杂参数](issues/04-complex-arguments.md)
- [ ] [0001-05 粘贴 C 声明](issues/05-paste-c-declaration.md)
- [ ] [0001-06 本地 PDB 补全](issues/06-local-pdb.md)
- [ ] [0001-07 生成 DllImport 代码](issues/07-generate-dllimport.md)
- [ ] [0001-08 生成 LibraryImport 代码](issues/08-generate-libraryimport.md)

## 需求进度

- [ ] 打开与识别（待实施）
- [ ] 补全函数签名（已确认通常只有 DLL，多语言示例为可选参考，待实施）
- [ ] 粘贴 C 声明（已确认函数原型、结构体、未知类型与宏的位置提示，待实施）
- [ ] 本地 PDB（已确认匹配校验、签名补全与来源说明，待实施）
- [ ] 签名复用（已确认同一 DLL 直接复用、换版本后重新确认，待实施）
- [ ] 选择与调用（已确认会话内连续调用与重新开始，待实施）
- [ ] 调用类型（已确认基础类型、缓冲区、输出参数和普通 C 结构体，待实施）
- [ ] 示例与批量导出（待实施）
