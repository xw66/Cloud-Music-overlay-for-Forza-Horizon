# Windows 端交接说明

## 当前目标

把 `alexandergwm/Cloud-Music-overlay-for-Forza-Horizon` 从 Forza Horizon 专用网易云悬浮窗，重构成通用游戏音乐 overlay。

核心边界：

- 只做桌面悬浮窗 overlay。
- 支持快捷键/手柄切歌。
- 不注入游戏进程。
- 不 hook DirectX/Vulkan/OpenGL。
- 不读取游戏内存。
- 不修改游戏文件。
- 不提供竞技游戏对局优势。

## 这轮已经做了什么

新增通用抽象：

- `Models/GameProfile.cs`
- `Models/ThemePack.cs`
- `Models/InputPreset.cs`
- `Models/OverlayMode.cs`

新增服务：

- `Services/GameProfileService.cs`
- `Services/ThemeService.cs`
- `Services/InputPresetService.cs`
- `Services/ForegroundGameDetector.cs`

接入点：

- `MainWindow.xaml.cs`
  - 创建 `GameProfileService`
  - 创建 `ForegroundGameDetector`
  - 生命周期启动前台游戏识别
  - 检测到游戏变化后切换 `ActiveGameProfileId / ThemeId / OverlayMode`

- `OverlayWindow.xaml`
  - 根节点 transform 从单一 `TranslateTransform` 改为 `TransformGroup`
  - 增加 `ScaleTransform`，给电台式滑动动画使用

- `OverlayWindow.xaml.cs`
  - 新增主题应用逻辑
  - 新增 `slide-radio` 横向滑入/替换动画
  - 仍保留原淡入动画作为 fallback

- `Models/OverlaySettings.cs`
  - schema 升级到 v2
  - 新增 `ActiveGameProfileId`
  - 新增 `ThemeId`
  - 新增 `OverlayMode`

- `Services/OverlaySettingsService.cs`
  - 新字段迁移和 normalize

新增测试：

- `tests/HorizonRadioOverlay.Tests/GameProfileServiceTests.cs`
- `tests/HorizonRadioOverlay.Tests/ThemeServiceTests.cs`
- 扩展 `OverlaySettingsServiceTests.cs`

## 内置 profile/theme

当前内置 profile：

- `generic-game`
- `forza-horizon`
- `league-of-legends`

当前内置 theme：

- `theme-minimal-dark`
- `theme-forza-horizon-radio`
- `theme-lol-rift`

LoL 只通过前台窗口/进程名识别，不接触游戏进程内部。

## Windows 端建议验证

在项目目录执行：

```powershell
dotnet restore
dotnet test
dotnet build
```

如果使用 Visual Studio：

1. 打开 `HorizonRadioOverlay.csproj`
2. 选择 Windows x64
3. Restore NuGet packages
4. Run tests
5. 启动应用，点“测试悬浮窗”

## 已知情况

当前 macOS 机器没有 `dotnet` 命令，所以这里没有实际跑过 WPF 编译和测试。

已经做过：

```bash
git diff --check
rg -n "[ \t]+$"
```

结果干净。

## 下一步建议

优先顺序：

1. 在 Windows 端跑 `dotnet test`，修任何编译问题。
2. 打开应用，点测试悬浮窗，确认横向滑动效果是否自然。
3. 开一个游戏窗口，验证自动 profile/theme 切换日志。
4. 再做 UI 设置页：显示当前识别游戏、当前主题、允许手动覆盖。
5. 后续把 profile/theme 从代码内置迁移到 JSON 文件。

