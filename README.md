# 网易云悬浮窗

一个 Windows 桌面工具：游戏中自定义快捷键转发网易云切歌，并显示透明悬浮窗（封面 + 歌名 + 歌手）。

支持**键盘**和**Xbox 手柄**快捷键。

## 截图

<!-- TODO: 放一张截图，建议截取主界面 + 游戏中的悬浮窗效果 -->

## 功能

- **透明悬浮窗**：点击穿透、置顶、不抢焦点
- **实时歌曲识别**：读取 `cloudmusic` 进程窗口标题，250ms 轮询
- **封面显示**：从网易云本地数据匹配下载并缓存
- **键盘快捷键映射**：自定义应用快捷键 -> 转发网易云快捷键
- **Xbox 手柄快捷键**：支持组合键（如 `LB+Left`），独立开关
- **悬浮窗自定义**：水平/垂直位置、缩放，可保存
- **设置持久化**：保存在 `%LOCALAPPDATA%\HorizonRadioOverlay\overlay-settings.json`
- **淡入淡出动画**：歌曲切换时自动弹出和隐藏

## 下载

前往 [Releases](https://github.com/xw66/Cloud-Music-overlay-for-Forza-Horizon/releases) 下载最新版本。

提供三个架构：
- `win-x64`：绝大多数 PC 选择此版本
- `win-x86`：32 位系统
- `win-arm64`：ARM 设备（如 Surface Pro X）

免安装 .NET 运行时，解压即用。

## 运行环境

- Windows 10 / 11
- 网易云音乐桌面版（进程名：`cloudmusic`）
- （手柄功能）Xbox 兼容手柄

## 使用说明

### 第一步：设置网易云快捷键

在网易云音乐中设置你想要的全局快捷键（如 `Ctrl+Alt+Left` 上一首、`Ctrl+Alt+Right` 下一首、`Ctrl+Alt+P` 播放/暂停）。

### 第二步：打开本工具

在"快捷键映射"中：
- **左侧**：填写你在游戏中按的键（应用快捷键）
- **右侧**：填写上一步在网易云设的快捷键（转发目标）

### 第三步：启动游戏

悬浮窗会在歌曲切换时自动弹出，5 秒后淡出。

## 快捷键说明

| 类型 | 示例 | 说明 |
|------|------|------|
| 键盘单键 | `L` | 单个字母或符号键 |
| 键盘组合 | `Ctrl+Shift+Left` | 修饰键 + 按键 |
| 手柄组合 | `LB+Left` | 按键用 `+` 连接 |
| 手柄特殊 | `LT+RT+Y` | 支持同时按多个键 |

## 开发运行

```powershell
dotnet run
```

## 打包发布

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 常见问题

**保存后不生效？**
确认状态栏提示"已保存并应用"。若提示注册失败，说明应用快捷键被其他程序占用，换一组即可。

**悬浮窗不显示？**
确认网易云正在播放歌曲，且进程名为 `cloudmusic`。

**手柄不能用？**
确认手柄已连接，"启用 Xbox 手柄快捷键"已勾选并保存。

## 技术栈

- .NET 8.0 WPF
- XInput (Xbox 手柄支持)
- Windows API (keybd_event 快捷键转发)

## 许可证

MIT
