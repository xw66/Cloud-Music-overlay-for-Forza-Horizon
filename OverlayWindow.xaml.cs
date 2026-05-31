using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay;

public sealed partial class OverlayWindow : Window
{
    private CancellationTokenSource? _hideCts;
    public const double BaseWidth = 210;
    public const double BaseHeight = 198;

    public OverlaySettings CurrentSettings { get; private set; } = new();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8;

    public OverlayWindow()
    {
        InitializeComponent();
        ApplySettings(CurrentSettings);
        TryHide();
    }

    public void ApplySettings(OverlaySettings settings)
    {
        CurrentSettings = new OverlaySettings
        {
            LeftPercent = Clamp(settings.LeftPercent, 0.0, 1.0),
            TopPercent = Clamp(settings.TopPercent, 0.0, 1.0),
            Scale = Clamp(settings.Scale, 0.8, 1.8),
            TitleColor = settings.TitleColor,
            ArtistColor = settings.ArtistColor,
            TitleOpacity = Clamp(settings.TitleOpacity, 0.0, 1.0),
            ArtistOpacity = Clamp(settings.ArtistOpacity, 0.0, 1.0)
        };

        double w = BaseWidth * CurrentSettings.Scale;
        double h = BaseHeight * CurrentSettings.Scale;

        try
        {
            int screenWidth = GetSystemMetrics(0);
            int screenHeight = GetSystemMetrics(1);

            int left = (int)((screenWidth - w) * CurrentSettings.LeftPercent);
            int top = (int)((screenHeight - h) * CurrentSettings.TopPercent);

            AppWindow.MoveAndResize(new RectInt32(left, top, (int)w, (int)h));
        }
        catch { }

        ApplyTextColors();
    }

    private void ApplyTextColors()
    {
        try
        {
            TitleText.Foreground = new SolidColorBrush(ParseColor(CurrentSettings.TitleColor));
            TitleText.Opacity = CurrentSettings.TitleOpacity;
        }
        catch { }

        try
        {
            ArtistText.Foreground = new SolidColorBrush(ParseColor(CurrentSettings.ArtistColor));
            ArtistText.Opacity = CurrentSettings.ArtistOpacity;
        }
        catch { }
    }

    private static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            return Color.FromArgb(255,
                byte.Parse(hex[0..2], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber));
        return Colors.White;
    }

    public Task ShowTrackAsync(TrackInfo track)
    {
        TitleText.Text = track.Name;
        ArtistText.Text = track.Artist;
        SetCover(track.CoverBytes);

        _hideCts?.Cancel();
        _hideCts = new CancellationTokenSource();

        TryShow();
        var pos = AppWindow.Position;
        AppWindow.MoveAndResize(new RectInt32(pos.X, pos.Y, AppWindow.Size.Width, AppWindow.Size.Height));
        BeginShowAnimation();
        ScheduleHide(_hideCts.Token);

        return Task.CompletedTask;
    }

    private async void ScheduleHide(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5000), token);
            if (!token.IsCancellationRequested) await HideWithAnimationAsync();
        }
        catch (OperationCanceledException) { }
    }

    private void SetCover(byte[]? coverBytes)
    {
        if (coverBytes == null || coverBytes.Length == 0)
        {
            CoverImage.Source = null;
            return;
        }
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(coverBytes);
            image.SetSource(stream.AsRandomAccessStream());
            CoverImage.Source = image;
        }
        catch { CoverImage.Source = null; }
    }

    private void BeginShowAnimation()
    {
        OverlayRoot.Opacity = 0;
        var anim = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(450)) };
        var sb = new Storyboard();
        sb.Children.Add(anim);
        Storyboard.SetTarget(anim, OverlayRoot);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Begin();
    }

    private async Task HideWithAnimationAsync()
    {
        var anim = new DoubleAnimation { From = 1, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(600)) };
        var sb = new Storyboard();
        sb.Children.Add(anim);
        Storyboard.SetTarget(anim, OverlayRoot);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Begin();
        await Task.Delay(610);
        TryHide();
    }

    private static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

    private void TryShow()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, SW_SHOWNA);
        }
        catch { }
    }

    private void TryHide()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, SW_HIDE);
        }
        catch { }
    }
}
