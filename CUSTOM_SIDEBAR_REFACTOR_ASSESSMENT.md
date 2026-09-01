# 自定义配置侧边栏重构评估

这个功能属于中等偏高难度，约 6.5/10。它不是单纯移动控件，而是要把“页面内部的预设切换”升级为“应用级动态导航”。

合理实现和完整验证预计需要：

- 核心开发：1～2 个工作日
- 配置迁移、异常场景和 UI 验证：0.5～1 个工作日
- 改动规模：约 4～6 个现有文件，建议新增 1～2 个服务/模型文件

## 推荐交互结构

左侧导航调整为：

```text
鸣潮

自定义游戏
  默认
  绝区零
  异环
  ＋ 添加自定义

敬请期待

设置
```

点击“添加自定义”后：

1. 输入侧边栏显示名称。
2. 创建一条空白自定义配置。
3. 将其立即加入左侧导航栏。
4. 自动打开对应的自定义页面。
5. 新页面保留现在自定义界面的全部功能。

原来的预设下拉框可以完全删除。重命名、删除建议移动到自定义页面顶部，或者放到侧边栏项目的右键菜单中。第一版建议放在页面顶部，实现更稳定，也更容易发现。

## 需要重构的模块

| 模块 | 目前情况 | 需要的调整 |
|---|---|---|
| 主窗口导航 | 菜单项全部写死在 XAML | 根据保存的自定义配置动态生成导航项 |
| 自定义页面 | 一个页面内部通过下拉框切换多个预设 | 一个页面只编辑一个指定配置 |
| 配置模型 | 主要使用预设名称确定当前项目 | 增加稳定、唯一的配置 ID |
| 配置保存 | 页面直接加载并覆盖整个预设列表 | 抽出集中式增删改查服务 |
| 页面导航 | 只根据固定字符串 Tag 跳转 | 将自定义配置 ID 作为页面参数传入 |
| 未保存状态 | 只处理下拉框切换时的未保存提示 | 处理侧边栏切换、设置页跳转和关闭窗口 |
| 配置迁移 | 已有 `CustomManifestPresets` 列表 | 自动为旧配置补 ID，并映射到侧边栏 |

### 1. 主窗口和侧边栏

当前导航栏完全写死在 [MainWindow.xaml](MainWindow.xaml)，跳转逻辑也使用固定的字符串 `Tag`，位于 [MainWindow.xaml.cs](MainWindow.xaml.cs)。

需要重构为：

- 保留固定的“鸣潮”“设置”等入口。
- 启动时读取 `CustomManifestPresets`。
- 为每条自定义配置动态创建 `NavigationViewItem`。
- 导航项保存配置 ID，而不是配置名称。
- 添加“＋ 添加自定义”入口。
- 配置重命名后立即刷新侧边栏文字。
- 删除当前配置后自动跳转到下一项或空白提示页。

侧边栏当前宽度只有 `150`，自定义名称可能显示不全，建议调整到约 `190～220`，并增加文字截断和 Tooltip。

### 2. 自定义页面参数化

当前 [CustomManifestPage.xaml.cs](Views/Pages/CustomManifestPage.xaml.cs) 在加载时读取全部预设，然后通过下拉框切换。

需要改成：

```csharp
ContentFrame.Navigate(
    typeof(CustomManifestPage),
    customManifestId);
```

页面在 `OnNavigatedTo` 中根据 ID 加载唯一配置。这样多个侧边栏项目可以复用同一个 `CustomManifestPage` 类型，但显示不同数据，不需要为每个游戏创建一套新的 XAML 文件。

[CustomManifestPage.xaml](Views/Pages/CustomManifestPage.xaml) 中现有的以下区域需要移除或替换：

- 预设下拉框
- 新建
- 另存为
- 当前下拉框切换逻辑

其余功能都可以保留：

- AppID、DepotID
- SteamDB 查询
- BuildID、Manifest
- 游戏 EXE
- 自定义启动器路径
- 复制启动命令
- 打开启动器
- 生成 ACF
- 日志输出

页面顶部建议新增：

- 当前自定义页面名称
- 重命名
- 删除
- 保存当前配置

### 3. 配置模型

现有模型在 [AppSettings.cs](Models/AppSettings.cs)。

建议为 `CustomManifestPreset` 增加：

```csharp
public string Id { get; set; } = Guid.NewGuid().ToString();
```

并新增：

```csharp
public string CurrentCustomManifestId { get; set; } = "";
```

不能继续把名称作为唯一标识，因为：

- 名称允许修改。
- 将来可能出现同名配置。
- 重命名时导航状态容易丢失。
- 页面跳转参数需要长期稳定。

`Name` 只负责侧边栏显示，`Id` 才负责查找、保存和导航。

### 4. 集中式配置服务

目前 [SettingsService.cs](Services/SettingsService.cs) 只负责整体读写 JSON，自定义页面内部直接操作完整列表。

建议新增类似：

```text
Services/CustomManifestService.cs
```

职责包括：

- `GetAll()`
- `GetById(id)`
- `Create(name)`
- `Update(preset)`
- `Rename(id, name)`
- `Delete(id)`
- `PresetsChanged` 事件

这样 `MainWindow` 和 `CustomManifestPage` 不会各自持有可能过期的列表，也能避免一个页面保存时覆盖其他页面刚写入的数据。

### 5. 未保存内容处理

这是本次重构最容易遗漏的部分。

现在只在下拉框切换时检查未保存改动。下拉框移到侧边栏后，用户可能直接点击：

- 鸣潮
- 另一个自定义项目
- 设置
- 添加自定义
- 关闭窗口

推荐两种方案：

- 简单可靠：输入变化后自动保存。
- 保留现有语义：导航前弹出“保存 / 放弃 / 取消”。

更推荐自动保存配置字段，但“生成 ACF”“打开启动器”等操作仍由按钮显式触发。这样侧边栏切换不会造成数据丢失，也能显著降低导航状态机复杂度。

### 6. 旧配置迁移

现有用户已经拥有 `CustomManifestPresets`，不能清空或要求重新配置。

启动时需要：

- 保留所有原有预设和字段。
- 为缺少 `Id` 的旧预设自动生成 ID。
- 将原来的 `CurrentCustomManifestName` 转换成 `CurrentCustomManifestId`。
- 迁移完成后保存一次。
- 原有“默认、绝区零、异环”等预设自动成为侧边栏项目。

建议暂时保留 `CurrentCustomManifest` 和 `CurrentCustomManifestName`，用于向后兼容，等后续大版本再删除。

## 主要风险

- 侧边栏切换时未保存内容丢失。
- 重命名后页面因为名称变化而找不到配置。
- 删除当前项目后导航指向已不存在的页面。
- 多个页面各自保存整个 `AppSettings`，导致新数据覆盖旧数据。
- 快速连续点击动态导航项造成重复导航。
- 导航栏折叠后，多个自定义项目使用相同图标，难以区分。
- 旧版 `settings.json` 没有 ID，需要稳定迁移。

## 推荐实施顺序

1. 给自定义配置增加唯一 ID 和迁移逻辑。
2. 抽出 `CustomManifestService`。
3. 将 `CustomManifestPage` 改为接收单个配置 ID。
4. 在主窗口动态生成自定义导航项。
5. 添加“添加自定义”、重命名和删除。
6. 删除旧预设下拉区域。
7. 实现自动保存或离开页面确认。
8. 验证旧配置升级、重命名、删除、重启恢复和多页面切换。
9. 分别编译并测试 x64、ARM64。

## 结论

现有数据结构已经具备多预设基础，因此不需要重写 SteamDB、ACF 生成、启动器或文件选择逻辑；主要重构集中在导航、配置身份、状态同步和页面生命周期。只要先引入稳定 ID 和集中式服务，这个功能可以比较干净地实现。
