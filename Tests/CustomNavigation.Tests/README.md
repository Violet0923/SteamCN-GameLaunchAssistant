# 游戏导航回归检查

运行：

```powershell
dotnet run --project Tests/CustomNavigation.Tests/CustomNavigation.Tests.csproj
```

此项目直接引用生产环境的配置模型和导航服务，无需额外测试框架。检查首次启动空列表、旧配置迁移、添加游戏、恢复选中游戏、排序持久化、无效排序拒绝及删除后的回退行为。

所有配置写入均使用测试输出目录中随机命名的文件，运行结束后自动清理，不读取或修改用户的真实设置。
