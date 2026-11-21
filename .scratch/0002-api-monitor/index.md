# 程序调用 C ABI DLL 监控进度

- [x] 需求范围与验收入口已确认，正式规格已发布。
- [x] 已确认拆分并建立 5 张工单，功能待实施。

## 实施工单

- [ ] [0002-01 搜索 Windows API](issues/01-search-windows-api.md)
- [ ] [0002-02 启动目标并核对调用](issues/02-monitor-launched-program.md)
- [ ] [0002-03 附加已有进程](issues/03-attach-running-program.md)
- [ ] [0002-04 用内置定义监控和补签名](issues/04-monitor-sdk-api.md)
- [ ] [0002-05 监控中的停用与热更新](issues/05-retain-monitor-session.md)

## 需求进度

- [ ] 选择目标与函数（已确认只采集所选进程，子进程可单独附加，待实施）
- [ ] 一次调用的记录（待实施）
- [ ] 记录保留期限（已确认插件热更新后仍可查看，退出主程序后清除，待实施）
- [ ] Windows API 目录（已确认广泛搜索 SDK 定义和按函数显示支持情况，待实施）
- [ ] 覆盖与验收（已确认宿主及目标位数、普通桌面和命令行程序及 .NET 调用原生 DLL，待实施）
