# Main Window Material UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将当前单页设置主窗口重构为左侧导航 + 多页面内容区的现代 Material Design 风格主窗口，同时保持悬浮窗、SMTC、歌词、快捷键与手柄业务逻辑不变。

**Architecture:** 保留现有 MainWindow 作为业务宿主，新增轻量 Shell 导航结构与 6 个页面级 UserControl，页面内部通过绑定到 MainWindow 或 Shell ViewModel 复用现有状态与事件。公共视觉风格统一沉淀到 Styles 资源字典，避免把大量界面样式继续堆在主窗口文件里。

**Tech Stack:** WPF, XAML ResourceDictionary, existing code-behind services, .NET 8 Windows

---

## File Map

- Modify: `E:\Documents\dpx6\App.xaml`
- Modify: `E:\Documents\dpx6\MainWindow.xaml`
- Modify: `E:\Documents\dpx6\MainWindow.xaml.cs`
- Create: `E:\Documents\dpx6\Models\NavigationItem.cs`
- Create: `E:\Documents\dpx6\ViewModels\MainShellViewModel.cs`
- Create: `E:\Documents\dpx6\Views\Pages\NowPlayingPage.xaml`
- Create: `E:\Documents\dpx6\Views\Pages\NowPlayingPage.xaml.cs`
- Create: `E:\Documents\dpx6\Views\Pages\FloatingSettingsPage.xaml`
- Create: `E:\Documents\dpx6\Views\Pages\FloatingSettingsPage.xaml.cs`
- Create: `E:\Documents\dpx6\Views\Pages\HotkeySettingsPage.xaml`
- Create: `E:\Documents\dpx6\Views\Pages\HotkeySettingsPage.xaml.cs`
- Create: `E:\Documents\dpx6\Views\Pages\ThemeSettingsPage.xaml`
- Create: `E:\Documents\dpx6\Views\Pages\ThemeSettingsPage.xaml.cs`
- Create: `E:\Documents\dpx6\Views\Pages\LogsPage.xaml`
- Create: `E:\Documents\dpx6\Views\Pages\LogsPage.xaml.cs`
- Create: `E:\Documents\dpx6\Views\Pages\AboutPage.xaml`
- Create: `E:\Documents\dpx6\Views\Pages\AboutPage.xaml.cs`
- Create: `E:\Documents\dpx6\Styles\MaterialTheme.xaml`
- Create: `E:\Documents\dpx6\Styles\Controls.xaml`
- Create: `E:\Documents\dpx6\Styles\Cards.xaml`
- Test: `E:\Documents\dpx6\tests\HorizonRadioOverlay.Tests\MainShellViewModelTests.cs`

## Execution Notes

- 不修改 `OverlayWindow.xaml.cs` 与悬浮窗逻辑。
- 不重写 `Services\SmtcTrackService.cs`、`Services\LyricsService.cs`、`Services\GlobalHotkeyService.cs`、`Services\GamepadInputService.cs`、`Services\NeteaseShortcutSender.cs` 的核心业务。
- 允许在 `MainWindow.xaml.cs` 内新增页面装配、导航和少量桥接属性，但不把新业务继续塞进去。
