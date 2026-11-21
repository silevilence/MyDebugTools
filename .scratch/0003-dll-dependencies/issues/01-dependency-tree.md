# 0003-01 — 当前 DLL 的依赖树

Status: ready-for-agent

**What to build:** 通过可独立启停的工作台页签，查看当前 DLL 声明的直接和间接依赖。

**Blocked by:** [0000-02 界面扩展点](../../0000-plugin-host/issues/02-view-extensions.md)、[0001-01 打开 DLL 查看导出](../../0001-dll-debugger/issues/01-browse-exports.md)

## 验收条件

- [ ] 打开一个 DLL 后切到依赖页签即可展开依赖树，更换当前文件后展示对应结果。
- [ ] 样例中的普通导入、延迟导入、共享依赖和缺失节点能区分；遇到循环时标明引用并结束该分支。
- [ ] 页面说明静态分析的范围。停用依赖插件后页签撤下，启用后在当前工作台恢复一次。
