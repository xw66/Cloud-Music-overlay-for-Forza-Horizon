using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay;

public partial class OverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolwindow = 0x00000080;

    private CancellationTokenSource? _hideCts;
    private readonly OverlayAnimationQueue _animQueue;
    public const double BaseWidth = 210;
    public const double BaseHeight = 198;

    public OverlaySettings CurrentSettings { get; private set; } = new();

    public OverlayWindow()
    {
        InitializeComponent();
        ApplySettings(CurrentSettings);
        Visibility = Visibility.Hidden;

        _animQueue = new OverlayAnimationQueue(Dispatcher, OnAnimationRequest);
        SourceInitialized += OverlayWindow_SourceInitialized;
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
            TitleOpacity = Clamp(settings.TitleOpacity, 0.2, 1.0),
            ArtistOpacity = Clamp(settings.ArtistOpacity, 0.2, 1.0),
            AlwaysShowOverlay = settings.AlwaysShowOverlay
        };

        Width = BaseWidth * CurrentSettings.Scale;
        Height = BaseHeight * CurrentSettings.Scale;

        double availableWidth = Math.Max(0, SystemParameters.PrimaryScreenWidth - Width);
        double availableHeight = Math.Max(0, SystemParameters.PrimaryScreenHeight - Height);
        Left = availableWidth * CurrentSettings.LeftPercent;
        Top = availableHeight * CurrentSettings.TopPercent;

        ApplyTextColors();
    }

    private void ApplyTextColors()
    {
        try
        {
            var titleColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(CurrentSettings.TitleColor);
            TitleText.Foreground = new System.Windows.Media.SolidColorBrush(titleColor);
            TitleText.Opacity = CurrentSettings.TitleOpacity;
        }
        catch { }

        try
        {
            var artistColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(CurrentSettings.ArtistColor);
            ArtistText.Foreground = new System.Windows.Media.SolidColorBrush(artistColor);
            ArtistText.Opacity = CurrentSettings.ArtistOpacity;
        }
        catch { }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public Task ShowTrackAsync(TrackInfo track)
    {
        _animQueue.Enqueue(track);
        return Task.CompletedTask;
    }

    private async Task OnAnimationRequest(TrackInfo track, long animId, CancellationToken token)
    {
        _hideCts?.Cancel();
        _hideCts = new CancellationTokenSource();

        bool isCurrentlyVisible = Visibility == Visibility.Visible && OverlayRoot.Opacity > 0.5;

        if (isCurrentlyVisible)
        {
            OverlayRoot.BeginAnimation(UIElement.OpacityProperty, null);
            double currentOpacity = OverlayRoot.Opacity;

            if (currentOpacity > 0.01)
            {
                var fadeOut = new DoubleAnimation
                {
                    From = currentOpacity,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                OverlayRoot.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                await Task.Delay(210, token);
                if (token.IsCancellationRequested) return;
            }
        }

        if (!token.IsCancellationRequested)
        {
            OverlayRoot.BeginAnimation(UIElement.OpacityProperty, null);
            OverlayRoot.Opacity = 0;

            TitleText.Text = track.Name;
            ArtistText.Text = track.Artist;
            SetCover(track.CoverBytes);
            ApplyTextColors();

            Show();
            Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            OverlayRoot.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            RootTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            RootTransform.Y = 0;

            if (!CurrentSettings.AlwaysShowOverlay)
            {
                ScheduleHide(_hideCts.Token);
            }
        }
    }

    private async void ScheduleHide(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5000), token);
            if (!token.IsCancellationRequested)
            {
                await HideWithAnimationAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
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
            BitmapImage image = new();

            using MemoryStream stream = new(coverBytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 200;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            CoverImage.Source = image;
        }
        catch
        {
            CoverImage.Source = null;
        }
    }

    private void OverlayWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GwlExstyle);
        SetWindowLong(hwnd, GwlExstyle, exStyle | WsExLayered | WsExTransparent | WsExToolwindow);
    }

    private async Task HideWithAnimationAsync()
    {
        DoubleAnimation fadeOut = new()
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(600),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        OverlayRoot.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        await Task.Delay(610);
        Hide();
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}
