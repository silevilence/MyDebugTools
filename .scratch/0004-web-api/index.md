# Web API 调试与接口定义管理进度

- [x] 需求范围与验收入口已确认，正式规格已发布。
- [x] 5 张工单已分别提交，Web API 插件已通过发布版统一验收。

## 实施工单

- [x] [0004-01 临时发送请求](issues/01-send-http-request.md)
- [x] [0004-02 保存接口定义](issues/02-save-api-definition.md)
- [x] [0004-03 发送正文与文件](issues/03-request-body-files.md)
- [x] [0004-04 查看请求历史](issues/04-request-history.md)
- [x] [0004-05 编辑 JSON 与查看响应](issues/05-json-and-response-views.md)

## 需求进度

- [x] 接口定义与目录。内部目录保存接口，实参独立于默认值。
- [x] 请求日志。跨重启保留最近 100 条，可单独清空。
- [x] 参数与正文。支持路径、查询、请求头、正文与上传文件。
- [x] 响应与错误。原文保留，可切换 JSON/XML 高亮与 HTML 静态预览。

## 统一验收记录

2026-11-21，Windows x64，.NET SDK 10.0.301。5 项实现依次对应本地提交 `0d229c8`、`7f279d6`、`dbd2b93`、`9028396`、`8d48074`。

| 验收入口 | 结果 |
| --- | --- |
| `dotnet build MyDebugTools.slnx -c Release --no-restore` | 0 警告、0 错误。 |
| `dotnet test MyDebugTools.slnx -c Release --no-build --no-restore -m:1` | 50 项通过，包括 8 项 Web API 用例。首次并行测试中，既有 x86 监控用例少计 1 次，单独复跑和最终全量均通过。 |
| `dotnet publish src/Desktop -c Release --no-build --no-restore -o artifacts/0004/publish` | 生成框架依赖发布版。管理页启用 Web API 后可从顶栏打开。 |
| `pwsh -NoProfile -File tests/Desktop.ps1 -WebApiOnly -Publish artifacts/0004/publish` | Windows UI 自动化与独立回显服务核对了下列结果。 |

- 自定义方法 CHECK 的两次调用分别收到 1001、1002；临时 GET 单独记入日志。重启后，三条日志保留各自的实参，重开定义仍是 1001。清空日志后可从原定义再次发送。主动保存默认值 1003 后重开可见。
- 服务实收路径含中文、空格和加号，自定义请求头与输入一致。空正文、JSON、XML、URL 编码表单、混合上传与二进制文件均核对了原文或字节。
- 请求 JSON 的中文错误列与原文一致。响应 JSON/XML 可切换格式化与原文；503 HTML 静态预览显示标题，服务未收到来自脚本、图片、样式或内嵌框架的探测请求。
- 连接失败、超时与主动取消分别显示。另在服务确认收到请求后关闭宿主，重启查看该条日志，结果为已取消。
- 使用过编辑器和预览后重载插件，默认值仍是 1003，卸载诊断显示旧版本已回收。正式日志上限取 100 条；定向测试以上限 3 验证淘汰顺序。

依赖全部从本机 NuGet 缓存还原。回显服务使用本机 Python 标准库，验证未下载外部资源。
