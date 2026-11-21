# MyDebugTools

## Agent skills

### Issue tracker

每个功能在本地 `.scratch/<NNNN>-<feature-slug>/` 目录下用 `spec.md` 记录需求，用同级的 `index.md` 记录完成进度。四位编号按需求首次提出的顺序从 `0000` 递增。实施期间随进展更新 `index.md`，直到需求完成。参见[任务跟踪约定](docs/agents/issue-tracker.md)。

### Triage labels

任务文件的 `Status:` 字段使用[五个默认分流标签](docs/agents/triage-labels.md)。

### Domain docs

本项目使用单一领域上下文。根目录的 `CONTEXT.md` 记录术语，`docs/adr/` 保存架构决策。参见[领域文档约定](docs/agents/domain.md)。
