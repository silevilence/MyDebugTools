# 本地任务跟踪

每个功能使用一个 `.scratch/<NNNN>-<feature-slug>/` 目录，例如 `.scratch/0000-plugin-host/`。四位编号按需求首次提出的顺序从 `0000` 递增，同一批需求按列出顺序分配。新增功能取下一个编号，已有功能的补充讨论沿用原编号，优先级或实施顺序变化时也保留原编号。

技能要求发布需求或任务时，在对应目录写入 Markdown 文件。读取任务时，按给定路径或该功能下的任务编号查找。

- 需求说明写入 `spec.md`。
- 创建 `spec.md` 时，在同一目录创建 `index.md`，按需求项列出当前进度。每当某项开始、完成或受阻，就同步更新对应条目；整个需求完成时，在 `index.md` 标明已完成。
- 每个实现任务单独写入 `issues/<NN>-<slug>.md`，在该功能内从 `01` 开始编号。
- 实现任务文件顶部用单独一行记录分流状态，例如 `Status: needs-triage`。可用值见[分流标签](triage-labels.md)。
- 将后续讨论追加到文件末尾的 `## Comments` 下。

## Wayfinder 探索任务

使用 `wayfinder` 探索某个功能时，探索记录放在该功能目录的 `map.md`。用 `Notes`、`Decisions-so-far` 和 `Fog` 分别记录笔记、已确定的结论和待解问题。

- 每个子任务写入同目录下的 `issues/NN-<slug>.md`，从 `01` 开始编号。正文写明要回答的问题，`Type:` 取值为 `research`、`prototype`、`grilling` 或 `task`。
- 有前置任务时，在文件顶部用 `Blocked by: 01, 02` 这样的格式列出编号。列出的任务全部标记为 `resolved` 后，才能领取。
- 选择编号最小、尚未解决、无人领取且没有阻塞的子任务。开始前写入 `Status: claimed` 并保存。
- 完成后，把答案追加到 `## Answer`，写入 `Status: resolved`，再将结论摘要和子任务链接追加到 `map.md` 的 `Decisions-so-far`。
