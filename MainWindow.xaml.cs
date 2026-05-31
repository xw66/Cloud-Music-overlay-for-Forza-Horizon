using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay;

public sealed partial class MainWindow : Window
{
    private readonly NeteaseLocalDataService _neteaseLocalDataService;
    private readonly SmtcTrackService _smtcTrackService;
    private readonly OverlaySettingsService _overlaySettingsService;
    private readonly OverlayWindow _overlayWindow;
    private readonly NeteaseShortcutSender _neteaseShortcutSender;
    private readonly GamepadInputService _gamepadInputService;
    private readonly UpdateService _updateService;
    private readonly OverlaySettings _activeSettings;

    public MainWindow(bool startMinimized = false)
    {
        InitializeComponent();

        AppWindow.Title = "网易云悬浮窗";
        AppWindow.Resize(new SizeInt32(1200, 720));

        _neteaseLocalDataService = new NeteaseLocalDataService();
        _smtcTrackService = new SmtcTrackService();
        _overlaySettingsService = new OverlaySettingsService();
        _overlayWindow = new OverlayWindow();
        _neteaseShortcutSender = new NeteaseShortcutSender();
        _gamepadInputService = new GamepadInputService();
        _updateService = new UpdateService();
        _activeSettings = _overlaySettingsService.Load();

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = "状态：WinUI 3 加载成功！";
        });

        LoadEmbeddedResources();
    }

    private void LoadEmbeddedResources()
    {
        try
        {
            var stream = typeof(MainWindow).Assembly
                .GetManifestResourceStream("HorizonRadioOverlay.Assets.Icons.icons8-github-50.png");
            if (stream != null)
            {
                var bitmap = new BitmapImage();
                bitmap.SetSource(stream.AsRandomAccessStream());
                GitHubImage.Source = bitmap;
            }
        }
        catch { }
    }

    private void Prev_Click(object sender, RoutedEventArgs e) { }
    private void Next_Click(object sender, RoutedEventArgs e) { }
    private void PlayPause_Click(object sender, RoutedEventArgs e) { }
    private void Refresh_Click(object sender, RoutedEventArgs e) { }
    private void CheckUpdate_Click(object sender, RoutedEventArgs e) { }

    private async void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/xw66/Cloud-Music-overlay-for-Forza-Horizon"));
    }
}
