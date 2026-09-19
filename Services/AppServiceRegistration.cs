using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.ViewModels;

namespace HorizonRadioOverlay.Services;

[SupportedOSPlatform("windows")]
public static class AppServiceRegistration
{
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        // 核心基础设施与底层诊断
        services.AddSingleton<DiagnosticService>();
        services.AddSingleton<OverlaySettingsService>();
        services.AddSingleton<CoverCacheService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<GamepadInputService>();
        services.AddSingleton<LyricsService>();
        services.AddSingleton<RemoteControlService>();

        // 歌词与时钟同步控制器
        services.AddSingleton<SmtcLyricTimingController>();
        services.AddSingleton<NeteaseLyricTimingController>();

        // 播放源实现与探针
        services.AddSingleton<NeteaseOfficialResolver>();
        services.AddSingleton<NeteaseShortcutSender>();
        services.AddSingleton<NeteaseMemoryPlaybackProbe>();
        services.AddSingleton<SmtcTrackService>();
        services.AddSingleton<NeteaseLocalDataService>();

        // 播放源列表与协调器
        services.AddSingleton<IPlaybackSource>(sp => new NeteasePlaybackSource(
            sp.GetRequiredService<NeteaseLocalDataService>(),
            sp.GetRequiredService<NeteaseShortcutSender>(),
            sp.GetRequiredService<NeteaseMemoryPlaybackProbe>()));

        services.AddSingleton<IPlaybackSource>(sp => new SmtcPlaybackSource(
            sp.GetRequiredService<SmtcTrackService>()));

        services.AddSingleton<PlaybackCoordinator>(sp => new PlaybackCoordinator(
            sp.GetServices<IPlaybackSource>()));

        services.AddSingleton<PlaybackSessionService>();

        // ViewModels
        services.AddSingleton<MainShellViewModel>();
        services.AddSingleton<NowPlayingViewModel>();
        services.AddSingleton<FloatingSettingsViewModel>();
        services.AddSingleton<ThemeSettingsViewModel>();
        services.AddSingleton<HotkeySettingsViewModel>();
        services.AddSingleton<RemoteControlViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<AboutViewModel>();

        // 悬浮窗与主窗口
        services.AddSingleton<OverlayWindow>();
        services.AddTransient<MainWindow>();

        return services;
    }
}

[SupportedOSPlatform("windows")]
public static class AppServices
{
    private static IServiceProvider? _current;

    public static IServiceProvider? Current
    {
        get => _current ??= BuildDefaultServiceProvider();
        set => _current = value;
    }

    public static T GetRequiredService<T>() where T : notnull
    {
        return (Current ?? throw new InvalidOperationException("ServiceProvider is not initialized.")).GetRequiredService<T>();
    }

    public static IServiceProvider BuildDefaultServiceProvider()
    {
        ServiceCollection services = new();
        services.AddAppServices();
        return services.BuildServiceProvider();
    }
}
