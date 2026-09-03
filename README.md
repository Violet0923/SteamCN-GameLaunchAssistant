# SteamCN-GameLaunchAssistant

鸣潮 Steam 助手 - 一款实现套壳游玩任何在steam上线的国内游戏，同时享受steam时长记录和截图等功能，电脑本地只需下载一份国服文件即可。
此处说明不一定会及时更新，详细文档请访问[鸣潮Steam助手帮助文档](https://www.iryougi.com/index.php/wutheringwavessteamhelper/)

## 主要功能

- **鸣潮专属页面**：一键生成鸣潮 ACF 配置 / 复制国服客户端启动命令 / 打开官方启动器
- **自定义添加游戏**：自由填写任意上线steam的游戏的 AppID/DepotID/BuildID/Manifest，生成 `appmanifest_<AppID>.acf` 与占位可执行文件；支持多预设管理（新建 / 另存为 / 重命名 / 删除）
- **Steam 全局配置**：Steam 安装路径、SteamLibrary 路径、SteamID 在「设置」中统一管理，全应用共享

## 系统要求

- Windows 操作系统（Windows 10/11）基于x64
- .NET 8.0 运行时
- Visual Studio Community 2022, 17.14.29 (March 2026)
- 已安装 Steam 客户端

## 技术说明
通过自动化配置游戏的acf文件欺骗steam已经下载游戏本体，随后通过启动命令诱导启动国服游戏本体

## 免责声明
本工具仅用于学习和研究目的。使用本工具产生的任何问题，开发者不承担责任。请支持正版游戏。
无法保证不会导致steam红信，至少从理论上来说符合steam的机制不会有问题。但也请自行承担相关风险和后果！！！

## 许可证
本项目遵循开源协议，具体请查看 LICENSE 文件。
