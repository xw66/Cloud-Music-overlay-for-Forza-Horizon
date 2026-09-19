# 网易云悬浮窗 v3.1.0 更新说明

本版本是一次**里程碑式的架构重构与稳定性升级**版本，全面治理了代码库内的核心技术债，完成了 MVVM 架构闭环、进程通信与托盘生命周期加固，同时显著降低了运行时 CPU 与 I/O 资源占用。

---

## 核心更新亮点

### 1. 全链路 MVVM 现代化重构
- 引入微软官方标准库 `CommunityToolkit.Mvvm` (8.4.2)；
- 建立 7 大功能模块 ViewModels（`NowPlaying`、`FloatingSettings`、`ThemeSettings`、`HotkeySettings`、`RemoteControl`、`Logs`、`About`），彻底将 XAML 与后台业务逻辑解耦；
- 消除 `MainWindow.xaml.cs` 中 50 余行穿透代理，实现属性双向绑定与强类型 RelayCommand。

### 2. 单实例命名管道 IPC 唤醒与托盘防假死
- 引入 `SingleInstanceIpcService` 命名管道服务，实现秒级跨进程通信；
- 重复双击启动时，新实例自动唤醒已有实例并穿透置顶；
- 智能检测主窗口位置，遇到多显示器断开或越界时自动复位至主屏中央，彻底解决托盘和无窗口后台假死问题。

### 3. 配置持久化原子落盘与容灾自愈机制
- `OverlaySettingsService` 升级为随机临时文件 + `Flush(flushToDisk: true)` 强制穿透 OS 物理刷盘 + `File.Move` 原生原子覆盖；
- 自动维护 `.bak` 镜像备份，遭遇系统断电或文件 0 字节损坏时秒级自愈恢复。

### 4. 播放轮询后台解耦与极致性能表现
- 彻底从 UI 主线程剥离高频 `DispatcherTimer`；
- 采用基于后台 Task 与 `SemaphoreSlim` 事件驱动的按需唤醒与状态派发机制；
- 运行时平均 CPU 占用降至 **0.15%**，稳定独占内存约 **110 MB**，绝不影响大型 3D 游戏帧率。

### 5. 诊断日志异步无锁 Channel 架构
- `DiagnosticService` 全面改造为基于 `System.Threading.Channels` 的高吞吐无锁生产者-消费者模式；
- 业务线程写日志降至 0ms 阻塞，后台单线程批量写盘与 Flush。

### 6. 工业级 IoC / DI 依赖注入容器
- 引入 `Microsoft.Extensions.DependencyInjection`；
- 建立统一服务拓扑中心 `AppServiceRegistration`，优雅管理 14+ 个核心服务、数据源管道与 ViewModels 生命周期。

### 7. 网易云内存探针 God Class 模块化拆分
- 将原 1906 行庞大巨石类解耦拆分为纯数学策略层、Win32 虚拟内存底层互操作层、时钟过滤流水线与精炼门面协调器。

### 8. 集中式 HttpClient 连接池生命周期管理
- 建立基于 `SocketsHttpHandler` 的集中提供者 `AppHttpClientProvider`，统一配置 15 分钟连接池生命周期以响应系统 DNS 变更，全面杜绝 Socket 端口耗尽。

---

## 质量与测试
- 单元测试套件扩充至 **293 项**，覆盖率大幅提升，**100% 绿灯通过**；
- 编译警告全面归零（0 警告、0 错误）。
