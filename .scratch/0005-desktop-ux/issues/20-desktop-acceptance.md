# 0005-20 — 发布版回归与首次使用者验收

Status: ready-for-agent

**What to build:** 在实际发布版上完成跨工具回归，并记录首次使用者能否独立完成任务。

**Blocked by:** [01](./01-task-home.md)、[02](./02-target-feedback.md)、[03](./03-plugin-table.md)、[04](./04-plugin-relations.md)、[05](./05-load-plugin-zip.md)、[06](./06-replace-plugin-zip.md)、[07](./07-remove-external-plugin.md)、[08](./08-dock-plugin-panels.md)、[09](./09-restore-dock-layout.md)、[10](./10-export-signature-drafts.md)、[11](./11-call-and-read-results.md)、[12](./12-copy-binding-preview.md)、[13](./13-trace-missing-dependency.md)、[14](./14-prepare-api-monitor.md)、[15](./15-read-monitor-session.md)、[16](./16-compose-http-request.md)、[17](./17-preserve-http-drafts.md)、[18](./18-dock-debug-results.md)、[19](./19-copy-tool-diagnostics.md)

对应[桌面体验规格](../spec.md)的验收旅程 A–M、四张任务卡。

## 验收条件

- [x] 复用本地插件、DLL、两种位数运行时和回显服务，从正常入口跑完 A–M。核对剪贴板、服务实收、调用次数、独立草稿及程序集回收，记录失败与复验结果。
- [x] 在默认 860 × 580 和最小 580 × 400 窗口验收。覆盖 100% 与 200% 缩放、浅深主题、高对比度、减少动态效果、键盘焦点与弹窗返回。记录关键截图，正文对比度至少 4.5:1，关键控件边界和状态标记至少 3:1。
- [x] 请 3 名符合规格的首次使用者独立完成 DLL 依赖、HTTP 与诊断、采集回看、外部插件与窗口整理四张任务卡。轮换起始卡，记录首次选择、卡点、提示、答案和实际粘贴文本。
- [x] 每张卡至少 2/3 独立完成；反馈文本能定位失败且不含测试凭据。两人卡在同一处时，修正后用未接触该任务的新使用者复验。真人结果与自动化结果分别登记。

验收记录见[发布版验收](../acceptance.md)。
