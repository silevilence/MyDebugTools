# 发布 Windows x64 稳定版

仓库的 Actions Secret `UPDATE_SIGNING_KEY` 保存 Ed25519 的 PKCS#8 PEM 私钥。首次发布前生成并备份密钥，后续版本沿用它。工作流从私钥提取 32 字节公钥编进宿主。更换私钥会使已安装的旧版拒绝新签名。

用 `generate-changelog` 准备版本和根目录的 `changelog.md`，例如标题 `## V1.2.3`。确保 `dotnet msbuild src/Desktop/MyDebugTools.fsproj -getProperty:Version` 输出相同的三段版本。提交后推送对应的正式标签。

Actions 构建框架依赖宿主，将安装包、`appcast.xml`、`appcast.xml.signature` 和记录提交与摘要的 `release-manifest.json` 上传到草稿。公开前逐项对比平台摘要。重跑时会补齐草稿。已公开的版本只需读取元数据和小型资产清单。

本地打包可在仓库根目录运行以下命令。使用新的输出目录，避免带入旧构建的文件。

```powershell
dotnet publish src/Desktop/MyDebugTools.fsproj -c Release --no-self-contained -o artifacts/local/publish
$version = dotnet msbuild src/Desktop/MyDebugTools.fsproj -getProperty:Version
./release/Build-Installer.ps1 -Publish artifacts/local/publish -Version $version -OutputDirectory artifacts/local
```

测试签名源可在构建测试宿主时设置 MSBuild 属性 `UpdatePublicKey` 和 `UpdateFeedUrl`。正式构建使用仓库最新 Release 的固定清单地址。安装包和说明均绑定到清单指定的标签。
