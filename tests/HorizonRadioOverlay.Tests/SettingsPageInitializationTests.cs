using System.Windows;
using System.Windows.Threading;
using System.Net;
using System.Net.Http;
using System.Reflection;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;
using HorizonRadioOverlay.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace HorizonRadioOverlay.Tests;

public sealed class SettingsPageInitializationTests
{
    [Fact]
    public async Task First_navigation_to_settings_preserves_saved_overlay_and_file()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                VerifySettingsNavigation();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static void VerifySettingsNavigation()
    {
        Application application = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (string resource in new[] { "MaterialTheme", "Controls", "Cards" })
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/HorizonRadioOverlay;component/Styles/{resource}.xaml")
            });
        }

        string directory = Path.Combine(Path.GetTempPath(), $"HRO_Pages_{Guid.NewGuid():N}");
        using RegistryKey? startupKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        object? originalStartup = startupKey?.GetValue("HorizonRadioOverlay");
        RegistryValueKind startupKind = originalStartup == null ? RegistryValueKind.String : startupKey!.GetValueKind("HorizonRadioOverlay");
        try
        {
            foreach (string firstPage in new[] { "FloatingSettings", "Theme" })
            {
                OverlaySettingsService settingsService = new(Path.Combine(directory, firstPage + ".json"));
                OverlaySettings expected = new()
                {
                    LeftPercent = 0.37,
                    TopPercent = 0.68,
                    Scale = 1.35,
                    TitleColor = "#123456",
                    ArtistColor = "#654321",
                    LyricsColor = "#ABCDEF",
                    TitleOpacity = 0.75,
                    ArtistOpacity = 0.65,
                    LyricsOpacity = 0.55,
                    EnableLyrics = false,
                    EnableGamepadHotkeys = false,
                    MinimizeToTray = false
                };
                settingsService.Save(expected);
                string savedJson = File.ReadAllText(settingsService.SettingsFilePath);
                ServiceCollection services = new();
                services.AddAppServices();
                services.AddSingleton(settingsService);
                services.AddSingleton(new PlaybackCoordinator([new IdlePlaybackSource()]));
                using DiagnosticService diagnostic = new(Path.Combine(directory, firstPage + ".log"));
                services.AddSingleton(diagnostic);
                using HttpClient httpClient = new(new OfflineUpdateHandler());
                services.AddSingleton(new UpdateService(httpClient));
                using ServiceProvider provider = services.BuildServiceProvider();
                MainWindow window = ActivatorUtilities.CreateInstance<MainWindow>(provider, false);
                try
                {
                    window.Opacity = 0;
                    window.ShowInTaskbar = false;
                    window.Show();
                    OverlayWindow overlay = provider.GetRequiredService<OverlayWindow>();
                    bool firstNavigation = true;
                    foreach (string page in new[] { firstPage, "FloatingSettings", "Theme", "Hotkeys", "RemoteControl" })
                    {
                        window.SetPreviewPage(page);
                        DispatcherFrame frame = new();
                        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                            () => frame.Continue = false);
                        Dispatcher.PushFrame(frame);

                        Assert.Equal(savedJson, File.ReadAllText(settingsService.SettingsFilePath));
                        if (firstNavigation)
                        {
                            // 首次修改也必须保留尚未打开页面中的设置。
                            if (firstPage == "FloatingSettings")
                            {
                                provider.GetRequiredService<FloatingSettingsViewModel>().ScalePercent = 145;
                                expected.Scale = 1.45;
                            }
                            else
                            {
                                provider.GetRequiredService<ThemeSettingsViewModel>().TitleOpacity = 80;
                                expected.TitleOpacity = 0.8;
                            }
                            savedJson = File.ReadAllText(settingsService.SettingsFilePath);
                            firstNavigation = false;
                        }

                        Assert.Equal(expected.LeftPercent, overlay.CurrentSettings.LeftPercent);
                        Assert.Equal(expected.TopPercent, overlay.CurrentSettings.TopPercent);
                        Assert.Equal(expected.Scale, overlay.CurrentSettings.Scale);
                        Assert.Equal(expected.TitleColor, overlay.CurrentSettings.TitleColor);
                        Assert.Equal(expected.ArtistColor, overlay.CurrentSettings.ArtistColor);
                        Assert.Equal(expected.LyricsColor, overlay.CurrentSettings.LyricsColor);
                        Assert.Equal(expected.TitleOpacity, overlay.CurrentSettings.TitleOpacity);
                        Assert.Equal(expected.ArtistOpacity, overlay.CurrentSettings.ArtistOpacity);
                        Assert.Equal(expected.LyricsOpacity, overlay.CurrentSettings.LyricsOpacity);
                        Assert.Equal(savedJson, File.ReadAllText(settingsService.SettingsFilePath));
                    }

                    provider.GetRequiredService<FloatingSettingsViewModel>().ScalePercent = 155;
                    OverlaySettings updated = settingsService.Load();
                    Assert.Equal(1.55, updated.Scale);
                    Assert.Equal(expected.LeftPercent, updated.LeftPercent);
                    Assert.Equal(expected.TopPercent, updated.TopPercent);
                    Assert.Equal(expected.TitleColor, updated.TitleColor);
                    Assert.Equal(expected.ArtistColor, updated.ArtistColor);
                    Assert.Equal(expected.LyricsColor, updated.LyricsColor);
                }
                finally
                {
                    typeof(MainWindow).GetMethod("RealExit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                }
            }
        }
        finally
        {
            // 旧实现可能在导航时修改开机启动设置，测试结束后恢复原值。
            if (!Equals(originalStartup, startupKey?.GetValue("HorizonRadioOverlay")))
            {
                if (originalStartup == null)
                    startupKey?.DeleteValue("HorizonRadioOverlay", false);
                else
                    startupKey!.SetValue("HorizonRadioOverlay", originalStartup, startupKind);
            }
            application.Shutdown();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class OfflineUpdateHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class IdlePlaybackSource : IPlaybackSource
    {
        public string Id => PlaybackSourceIds.Netease;
        public string DisplayName => "Test player";
        public bool IsAvailable => true;
        public PlaybackSourceCapabilities Capabilities => PlaybackSourceCapabilities.TrackMetadata;
        public Task<TrackInfo?> GetCurrentTrackAsync() => Task.FromResult<TrackInfo?>(null);
        public Task<(TimeSpan Position, bool IsPlaying)?> GetPlaybackStateAsync()
            => Task.FromResult<(TimeSpan Position, bool IsPlaying)?>(null);
        public Task<PlaybackCommandStatus> ExecuteAsync(PlaybackCommand command) => Task.FromResult(PlaybackCommandStatus.Failed);
    }
}
