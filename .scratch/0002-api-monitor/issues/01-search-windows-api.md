# 0002-01 — 搜索 Windows API

Status: ready-for-agent

**What to build:** 无需选择 DLL，在监控页搜索内置 Windows SDK API 目录，查看函数和字段的支持情况。

**Blocked by:** [0000-01 单入口与插件管理](../../0000-plugin-host/issues/01-desktop-entry.md)

## 验收条件

- [ ] 可按名称从多个 SDK 功能类别查到定义，验收记录注明使用的目录来源与范围。
- [ ] 对完整解码、部分解码和暂不支持的样例，分别标明函数是否可监控、哪些参数可解码以及限制原因。
- [ ] 用户在开始采集前即可查看这些信息并选择支持的函数。
