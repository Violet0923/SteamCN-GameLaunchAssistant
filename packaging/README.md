# 构建安装包

在 Windows x64 上安装 .NET 8 SDK、Windows SDK 和 Inno Setup 6.5 或更新的 6.x 版本后运行：

```powershell
./scripts/Publish-Release.ps1
# 如编译器安装在其他目录：
./scripts/Publish-Release.ps1 -InnoCompiler 'D:\Tools\Inno Setup 6\ISCC.exe'
```

发布前同步 `version.json`、项目 Version、`AppInfo.Version` 和 `Package.appxmanifest` 的版本；脚本会检查一致性。界面发布阶段使用 `AppInfo.Channel`。

每次构建使用独立的 `Output/v<版本>-<随机标识>/` 目录，生成：

- `publish/`：自包含 .NET 和 Windows App SDK 的 Windows x64 程序。
- `SteamCN-GameLaunchAssistant-v<版本>-win-x64-setup.exe`：中英文安装包。
- `SHA256SUMS.txt`：安装包校验值。

脚本只构建本地产物；提交、打标签和上传 GitHub Release 单独进行。发布文件不包含 PDB 或日志，安装器包含完整发布目录以避免手写 DLL 清单漏项。

自包含部署使用 Windows App SDK 的注册免安装初始化，跳过依赖系统已安装运行库的手动 Bootstrap 调用；开发时的非自包含构建仍保留原初始化路径。参见 [Microsoft 部署文档](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)。

## 升级兼容

安装器继续使用原 AppId，新安装默认目录为 `SteamCN-GameLaunchAssistant`，升级沿用已安装目录；只移除明确列出的旧程序文件和旧快捷方式，不删除用户配置或日志。

配置目录继续使用 `%APPDATA%/WutheringWavesSteamHelper`。旧单实例互斥标识保留，避免新旧程序同时修改设置。项目源码命名空间改为合法 C# 标识符 `SteamCNGameLaunchAssistant`，产物使用带连字符的名称。

## 中文安装界面

`languages/ChineseSimplified.isl` 来自 [kira-96 的翻译项目](https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation)，固定在提交 `1ff90acc4ed4aee82b1cda43253243deee3daed4`，以 MIT 许可分发，许可证位于 `languages/LICENSE.txt`。构建无需在线下载翻译。
