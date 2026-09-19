using System.Runtime.Versioning;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;
using HorizonRadioOverlay.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HorizonRadioOverlay.Tests;

[SupportedOSPlatform("windows")]
public class DependencyInjectionTests
{
    [Fact]
    public void AddAppServices_RegistersCoreInfrastructureServices()
    {
        ServiceCollection services = new();
        services.AddAppServices();
        using ServiceProvider sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<DiagnosticService>());
        Assert.NotNull(sp.GetService<OverlaySettingsService>());
        Assert.NotNull(sp.GetService<CoverCacheService>());
        Assert.NotNull(sp.GetService<UpdateService>());
        Assert.NotNull(sp.GetService<GamepadInputService>());
        Assert.NotNull(sp.GetService<LyricsService>());
        Assert.NotNull(sp.GetService<RemoteControlService>());
    }

    [Fact]
    public void AddAppServices_RegistersLyricTimingAndDataServices()
    {
        ServiceCollection services = new();
        services.AddAppServices();
        using ServiceProvider sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<SmtcLyricTimingController>());
        Assert.NotNull(sp.GetService<NeteaseLyricTimingController>());
        Assert.NotNull(sp.GetService<NeteaseOfficialResolver>());
        Assert.NotNull(sp.GetService<NeteaseShortcutSender>());
        Assert.NotNull(sp.GetService<NeteaseMemoryPlaybackProbe>());
        Assert.NotNull(sp.GetService<SmtcTrackService>());
        Assert.NotNull(sp.GetService<NeteaseLocalDataService>());
    }

    [Fact]
    public void AddAppServices_RegistersPlaybackSourcesAndCoordinator()
    {
        ServiceCollection services = new();
        services.AddAppServices();
        using ServiceProvider sp = services.BuildServiceProvider();

        var sources = sp.GetServices<IPlaybackSource>().ToList();
        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, s => s is NeteasePlaybackSource);
        Assert.Contains(sources, s => s is SmtcPlaybackSource);

        var coordinator = sp.GetService<PlaybackCoordinator>();
        Assert.NotNull(coordinator);
        Assert.Equal(2, coordinator.Sources.Count);

        var sessionService = sp.GetService<PlaybackSessionService>();
        Assert.NotNull(sessionService);
    }

    [Fact]
    public void AddAppServices_RegistersViewModelsAsSingletons()
    {
        ServiceCollection services = new();
        services.AddAppServices();
        using ServiceProvider sp = services.BuildServiceProvider();

        var shell1 = sp.GetRequiredService<MainShellViewModel>();
        var shell2 = sp.GetRequiredService<MainShellViewModel>();
        Assert.Same(shell1, shell2);

        var nowPlaying1 = sp.GetRequiredService<NowPlayingViewModel>();
        var nowPlaying2 = sp.GetRequiredService<NowPlayingViewModel>();
        Assert.Same(nowPlaying1, nowPlaying2);

        var floating1 = sp.GetRequiredService<FloatingSettingsViewModel>();
        var floating2 = sp.GetRequiredService<FloatingSettingsViewModel>();
        Assert.Same(floating1, floating2);

        var theme1 = sp.GetRequiredService<ThemeSettingsViewModel>();
        var theme2 = sp.GetRequiredService<ThemeSettingsViewModel>();
        Assert.Same(theme1, theme2);

        var hotkey1 = sp.GetRequiredService<HotkeySettingsViewModel>();
        var hotkey2 = sp.GetRequiredService<HotkeySettingsViewModel>();
        Assert.Same(hotkey1, hotkey2);

        var remote1 = sp.GetRequiredService<RemoteControlViewModel>();
        var remote2 = sp.GetRequiredService<RemoteControlViewModel>();
        Assert.Same(remote1, remote2);

        var logs1 = sp.GetRequiredService<LogsViewModel>();
        var logs2 = sp.GetRequiredService<LogsViewModel>();
        Assert.Same(logs1, logs2);

        var about1 = sp.GetRequiredService<AboutViewModel>();
        var about2 = sp.GetRequiredService<AboutViewModel>();
        Assert.Same(about1, about2);
    }

    [Fact]
    public void AddAppServices_CoreSingletonsMaintainInstanceIdentity()
    {
        ServiceCollection services = new();
        services.AddAppServices();
        using ServiceProvider sp = services.BuildServiceProvider();

        Assert.Same(sp.GetRequiredService<DiagnosticService>(), sp.GetRequiredService<DiagnosticService>());
        Assert.Same(sp.GetRequiredService<OverlaySettingsService>(), sp.GetRequiredService<OverlaySettingsService>());
        Assert.Same(sp.GetRequiredService<CoverCacheService>(), sp.GetRequiredService<CoverCacheService>());
        Assert.Same(sp.GetRequiredService<UpdateService>(), sp.GetRequiredService<UpdateService>());
        Assert.Same(sp.GetRequiredService<GamepadInputService>(), sp.GetRequiredService<GamepadInputService>());
        Assert.Same(sp.GetRequiredService<LyricsService>(), sp.GetRequiredService<LyricsService>());
        Assert.Same(sp.GetRequiredService<RemoteControlService>(), sp.GetRequiredService<RemoteControlService>());
        Assert.Same(sp.GetRequiredService<PlaybackCoordinator>(), sp.GetRequiredService<PlaybackCoordinator>());
        Assert.Same(sp.GetRequiredService<PlaybackSessionService>(), sp.GetRequiredService<PlaybackSessionService>());
    }

    [Fact]
    public void AppServices_CurrentAndGetRequiredService_WorkCorrectly()
    {
        var originalCurrent = AppServices.Current;
        try
        {
            ServiceCollection services = new();
            services.AddAppServices();
            using ServiceProvider sp = services.BuildServiceProvider();
            AppServices.Current = sp;

            Assert.Same(sp, AppServices.Current);
            var diagnostic = AppServices.GetRequiredService<DiagnosticService>();
            Assert.NotNull(diagnostic);
            Assert.Same(sp.GetRequiredService<DiagnosticService>(), diagnostic);
        }
        finally
        {
            AppServices.Current = originalCurrent;
        }
    }
}
