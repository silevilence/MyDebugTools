# C ABI DLL 调试进度

- [x] 需求范围与验收入口已确认，正式规格已发布。
- [x] 已确认拆分并建立 8 张工单，功能待实施。

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
