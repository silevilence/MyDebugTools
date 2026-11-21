# 程序调用 C ABI DLL 监控进度

- [x] 需求范围与验收入口已确认，正式规格已发布。
- [x] 已确认拆分并建立 5 张工单，功能待实施。

## 实施工单

- [x] [0002-01 搜索 Windows API](issues/01-search-windows-api.md)
- [x] [0002-02 启动目标并核对调用](issues/02-monitor-launched-program.md)
- [x] [0002-03 附加已有进程](issues/03-attach-running-program.md)
- [ ] [0002-04 用内置定义监控和补签名](issues/04-monitor-sdk-api.md)
- [ ] [0002-05 监控中的停用与热更新](issues/05-retain-monitor-session.md)

## 需求进度

- [x] 选择目标与函数（启动、附加及父子进程隔离均已验证）
- [ ] 一次调用的记录（启动采集已验证，待验收并发与缺失状态）
- [ ] 记录保留期限（已确认插件热更新后仍可查看，退出主程序后清除，待实施）
- [x] Windows API 目录（1410 条，按字段预告支持程度）
- [ ] 覆盖与验收（已确认宿主及目标位数、普通桌面和命令行程序及 .NET 调用原生 DLL，待实施）
