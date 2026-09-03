# SteamCN-GameLaunchAssistant

Steam国服游戏启动助手，用于在 Steam 库中添加和管理国服游戏的启动配置，通过 Steam 启动本地国服游戏，使用时长记录和截图等功能；电脑本地只需下载一份国服游戏文件。
此处说明不一定会及时更新，详细文档请访问[鸣潮Steam助手帮助文档](https://www.iryougi.com/index.php/wutheringwavessteamhelper/)

## 主要功能

- **游戏导航**：不再提供默认鸣潮入口；启动时恢复上次选择的自定义游戏，无有效记录时打开第一个游戏。尚未添加游戏或删除最后一个游戏后，显示「添加游戏」按钮；已添加的游戏可在侧边栏拖动排序，顺序自动保存。
- **自定义添加游戏**：填写已在 Steam 上线游戏的 AppID，自动获取名称、安装目录、占位 EXE、DepotID、BuildID 和 Manifest，也可手动编辑；生成 `appmanifest_<AppID>.acf` 与占位可执行文件，支持多预设管理（新建 / 另存为 / 重命名 / 删除）。
- **Steam 全局配置**：Steam 安装路径、SteamLibrary 路径、SteamID 在「设置」中统一管理，全应用共享
- **外观入口**：侧边栏保留「外观」，点击显示「敬请期待」。

拖动游戏时，横线标出插入位置；松开鼠标后保存顺序，按 Esc 或拖出列表可取消。可拖到「已添加的游戏」标题置顶，或拖到「添加游戏」处置底。

### 根据 AppID 自动填充

在自定义页面填写 AppID，点击「自动获取游戏信息」，即可通过 steamcmd.net 获取显示名称、安装目录、Steam 占位 EXE、DepotID 以及公开分支的 BuildID / Manifest。名称优先使用页面所选语言，安装目录保留 Steam 登记值。真实游戏 EXE / 启动器位置仍需本地选择。

唯一候选会直接填入；多个 Depot 或启动 EXE 会显示候选列表，同一个 EXE 的不同启动参数会合并展示。选择 Depot 时会同步对应 Manifest。当前 ACF 仍只支持一个 Depot，多个 Depot 可能需要共同使用，请核对后选择；此功能不保证生成完整安装配置。

网络失败或缺失字段会保留原值并标明未验证。请求期间修改输入、切换语言、切换预设或离开页面，会丢弃过期结果。获取不会直接写入 Steam 文件，配置保存继续沿用现有按钮及离开页面时的自动保存流程。

扩展设计、边界和验收说明见 [AppID 自动填充需求](APPID_AUTO_FILL_REQUIREMENTS.md)。开发验证方式见 [元数据回归检查](Tests/SteamAppInfo.Tests/README.md) 和 [游戏导航回归检查](Tests/CustomNavigation.Tests/README.md)。

## 系统要求

- Windows 操作系统（Windows 10/11）基于x64
- 官方安装包包含 .NET 和 Windows App SDK 运行库
- 已安装 Steam 客户端
- Steam 游戏库内已入库对应游戏

## 技术说明

程序文件名为 `SteamCN-GameLaunchAssistant.exe`，项目入口为 `SteamCN-GameLaunchAssistant.sln`。中文显示名称为「Steam国服游戏启动助手」。旧版本升级继续读取原配置目录，保留已添加游戏和排序。

开发环境使用 .NET 8 SDK、Windows SDK 和支持 WinUI 3 的 Visual Studio。安装包构建方式见 [发布说明](packaging/README.md)。

通过自动化配置游戏的acf文件欺骗steam已经下载游戏本体，随后通过启动命令诱导启动国服游戏本体

## 免责声明

本工具仅用于学习和研究目的。使用本工具产生的任何问题，开发者不承担责任。请支持正版游戏。
无法保证不会导致steam红信，至少从理论上来说符合steam的机制不会有问题。但也请自行承担相关风险和后果！！！

## 许可证

本项目遵循开源协议，具体请查看 LICENSE 文件。
