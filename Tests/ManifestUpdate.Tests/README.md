# 一键更新与 Manifest 备份回归检查

```powershell
dotnet run --project Tests/ManifestUpdate.Tests/ManifestUpdate.Tests.csproj
```

检查自动 Depot／EXE 决策、默认安装目录备份路径、覆盖前备份、内容相同时跳过写入，以及每个 AppID 最多两份且最长 30 天的滚动清理规则。实际写入检查使用随机临时目录，运行结束后删除。
