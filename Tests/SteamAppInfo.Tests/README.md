# 游戏信息回归检查

此项目直接链接生产环境的模型、HTTP 服务、解析器、筛选策略和路径校验代码，不依赖 WinUI、额外测试包或真实 Steam 安装，不修改用户预设和游戏文件。

从仓库根目录运行离线检查：

```powershell
dotnet run --project Tests/SteamAppInfo.Tests/SteamAppInfo.Tests.csproj
```

可选的真实接口检查（需要网络，仅查询公开元数据）：

```powershell
dotnet run --project Tests/SteamAppInfo.Tests/SteamAppInfo.Tests.csproj -- --live
```

离线检查覆盖本地化名称与目录分离、平台／架构／语言／DLC 筛选、多 Depot、多 EXE、同路径不同参数去重、超大 Manifest ID、分支切换、缺失数据、危险路径、AppID 校验、替换 HTTP 实现、错误响应、超时及请求取消。

真实接口检查只验证数据能被新实现获取和解析，不固定 BuildID / Manifest 等会更新的值。测试数据中的示例 AppID 不参与生产逻辑。

页面交互另需在 Windows 上核对：

1. 输入 AppID 后获取，检查自动填写、提示和多候选列表。
2. 更换 Depot，检查 Manifest 同步；填入未知 Depot，旧 Manifest 应清除。
3. 请求进行中修改字段、切换语言／预设或离开页面，旧结果不应覆盖新表单。
4. 重复获取、手动编辑后保存，并重新进入页面确认预设兼容。
5. 验证获取本身不生成 ACF 或占位文件。离开页面仍会按既有行为保存预设。
