# 通过 GitHub Release 更新宿主

Status: ready-for-agent

## 用户流程

1. 启动桌面宿主后在后台检查稳定版，用户可以立即开始工作。只有远端数字版本更高时才展示版本号和该版 Release 的说明。
2. 用户可稍后处理。只有点「立即更新」才下载 NSIS 安装包；下载或校验失败时保留正在使用的宿主，显示原因并允许重试。
3. 若仍在采集，展示目标和受影响的采集会话，提供「继续采集，稍后更新」与「结束采集并更新」。关闭提示等同稍后更新。交接给安装包前重新检查采集状态，等待用户同意结束的所有会话停止成功。
4. 校验通过、宿主可正常退出后，交给 NSIS 覆盖升级。再次打开可核对新版和原有持久数据。退出时按现有规则清理 DLL 监控记录。

## 选型与发布

- NSIS 的 OnlineUpdate 示例解决安装程序运行期间的版本查询。选用 NetSparkle 的 .NET 核心库处理宿主内的版本检查、下载和签名校验，沿用现有 FuncUI 提示采集状态，升级复用发布安装包。实施时用当前 .NET 10 和 Avalonia 组合验证集成。[NSIS 示例](https://nsis.sourceforge.io/Check_online_for_Updates) · [NetSparkle 官方说明](https://github.com/NetSparkleUpdater/NetSparkle)
- 每个 Release 携带固定名称的更新清单及其签名。宿主经最新正式版的固定资产地址发现清单；清单里的 NSIS 包和版本说明指向具体标签，检查与下载之间即使又发布一版也对应同一个版本。[GitHub 最新版资产地址](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases)
- 同一份 changelog 版本正文供 Release 和应用内说明使用。发布端以 Ed25519 私钥签名，宿主内置公钥，验证清单和安装包后执行安装。私钥由 Actions Secret 提供。签名资产齐备后再公开该版本。

## 验收

- 用两份本地版本和签名清单启动真实宿主，观察新版本提示、延后及再次检查。覆盖同版、远端较旧、网络中断、清单或安装包被改动；检查失败时用户仍能继续工作。
- 从旧版安装入口完成一次新版升级。分别验证继续采集及结束采集两条路径，确认停止失败时仍能看到原因。对照本地 HTTP 实收请求，确认后台检查和点击稍后均未下载安装包。
- 真实 Release 验证一次下载和升级，余下用例复用已有安装包、缓存及本地测试源。[发布规格](../0006-github-release/spec.md)
