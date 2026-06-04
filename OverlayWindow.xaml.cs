using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
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
    private readonly List<CoverFlowAlbum> _coverFlowHistory = new();
    public const double BaseWidth = 210;
    private const double CoverFlowWidth = 760;
    public const double BaseHeight = 198;
    private const double CoverFlowHeight = 378;
    private const int CoverFlowCapacity = 9;
    private const int CoverFlowCenterIndex = 3;
    private const double CoverFlowCenterX = 312;
    private const double CenterCoverSize = 128;

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
            AlwaysShowOverlay = settings.AlwaysShowOverlay,
            EnableLyrics = settings.EnableLyrics,
            EnableCoverWingEffect = settings.EnableCoverWingEffect,
            LyricsColor = settings.LyricsColor,
            LyricsOpacity = Clamp(settings.LyricsOpacity, 0.2, 1.0)
        };

        double layoutWidth = CurrentSettings.EnableCoverWingEffect ? CoverFlowWidth : BaseWidth;
        double layoutHeight = CurrentSettings.EnableCoverWingEffect ? CoverFlowHeight : BaseHeight;
        OverlayRoot.Width = layoutWidth;
        OverlayRoot.Height = layoutHeight;
        CoverRow.Height = new GridLength(CurrentSettings.EnableCoverWingEffect ? 286 : 106);
        CoverFlowViewport.Visibility = CurrentSettings.EnableCoverWingEffect ? Visibility.Visible : Visibility.Collapsed;
        CoverFrame.Visibility = CurrentSettings.EnableCoverWingEffect ? Visibility.Collapsed : Visibility.Visible;
        InfoBackdrop.Visibility = CurrentSettings.EnableCoverWingEffect ? Visibility.Collapsed : Visibility.Visible;
        InfoPanel.Width = 188;
        InfoPanel.HorizontalAlignment = CurrentSettings.EnableCoverWingEffect ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        InfoPanel.Margin = CurrentSettings.EnableCoverWingEffect
            ? new Thickness(0, 4, 0, 0)
            : new Thickness((BaseWidth - 188) / 2.0, 4, 0, 0);

        Width = layoutWidth * CurrentSettings.Scale;
        Height = layoutHeight * CurrentSettings.Scale;

        double availableWidth = Math.Max(0, SystemParameters.PrimaryScreenWidth - Width);
        double availableHeight = Math.Max(0, SystemParameters.PrimaryScreenHeight - Height);
        Left = availableWidth * CurrentSettings.LeftPercent;
        Top = availableHeight * CurrentSettings.TopPercent;

        ApplyTextColors();
    }

    public void ApplyTextColors()
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

        try
        {
            var lyricsColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(CurrentSettings.LyricsColor);
            LyricsText.Foreground = new System.Windows.Media.SolidColorBrush(lyricsColor);
            LyricsText.Opacity = CurrentSettings.LyricsOpacity;
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
            UpdateCoverFlowAlbums(null);
            return;
        }

        try
        {
            BitmapSource image = CreateSquareCoverSource(coverBytes);

            CoverImage.Source = image;
            UpdateCoverFlowAlbums(image);
        }
        catch
        {
            CoverImage.Source = null;
            UpdateCoverFlowAlbums(null);
        }
    }

    private void UpdateCoverFlowAlbums(ImageSource? coverImage)
    {
        string title = string.IsNullOrWhiteSpace(TitleText.Text) ? "正在播放" : TitleText.Text;
        string artist = string.IsNullOrWhiteSpace(ArtistText.Text) ? "网易云音乐" : ArtistText.Text;
        string key = GetCurrentTrackKey();

        CoverFlowAlbum currentAlbum = new()
        {
            Key = key,
            Title = title,
            Artist = artist,
            CoverImage = coverImage ?? CreatePlaceholderCover(title, artist)
        };

        int existingIndex = _coverFlowHistory.FindIndex(album => string.Equals(album.Key, key, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            _coverFlowHistory.RemoveAt(existingIndex);
        }

        _coverFlowHistory.Add(currentAlbum);
        if (_coverFlowHistory.Count > CoverFlowCapacity)
        {
            _coverFlowHistory.RemoveAt(0);
        }

        RenderCoverFlow(currentAlbum);
    }

    private string GetCurrentTrackKey()
    {
        string title = string.IsNullOrWhiteSpace(TitleText.Text) ? "正在播放" : TitleText.Text.Trim();
        string artist = string.IsNullOrWhiteSpace(ArtistText.Text) ? "网易云音乐" : ArtistText.Text.Trim();
        return $"{title}|{artist}";
    }

    private void RenderCoverFlow(CoverFlowAlbum currentAlbum)
    {
        CoverFlowViewport.Children.Clear();
        AddCoverFlowLights();

        List<CoverFlowAlbum> previous = _coverFlowHistory
            .Where(album => !string.Equals(album.Key, currentAlbum.Key, StringComparison.Ordinal))
            .ToList();

        List<CoverFlowAlbum> history = previous.TakeLast(8).ToList();
        int leftCount = Math.Min(3, history.Count / 2 + history.Count % 2);
        int rightCount = Math.Min(4, history.Count - leftCount);
        List<CoverFlowAlbum> leftAlbums = history.Take(leftCount).ToList();
        List<CoverFlowAlbum> rightAlbums = history.Skip(leftCount).Take(rightCount).ToList();

        List<(CoverFlowAlbum Album, int Offset)> arranged = new();
        for (int i = 0; i < leftAlbums.Count; i++)
        {
            arranged.Add((leftAlbums[i], i - leftAlbums.Count));
        }

        for (int i = 0; i < rightAlbums.Count; i++)
        {
            arranged.Add((rightAlbums[i], i + 1));
        }

        foreach ((CoverFlowAlbum album, int offset) in arranged.OrderByDescending(item => Math.Abs(item.Offset)))
        {
            AddAlbumPlane(album.CoverImage, CreateCoverFlowSlot(offset));
        }

        AddAlbumPlane(currentAlbum.CoverImage, CreateCenterCoverFlowSlot());
    }

    private void AddCoverFlowLights()
    {
        Model3DGroup lights = new();
        lights.Children.Add(new AmbientLight(Color.FromRgb(214, 220, 230)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(255, 255, 255), new Vector3D(-0.2, -0.1, -1.0)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(150, 165, 190), new Vector3D(0.35, -0.05, -1.0)));

        CoverFlowViewport.Children.Add(new ModelVisual3D
        {
            Content = lights
        });
    }

    private void AddAlbumPlane(
        ImageSource? image,
        (double X, double Y, double Z, double Size, double Scale, double Rotation, double Opacity, int ZIndex, double Brightness) slot)
    {
        if (image == null)
        {
            return;
        }

        double width = slot.Size;
        double height = slot.Size;
        double halfWidth = width / 2.0;
        double halfHeight = height / 2.0;

        MeshGeometry3D mesh = new()
        {
            Positions = new Point3DCollection
            {
                new(-halfWidth, -halfHeight, 0),
                new(halfWidth, -halfHeight, 0),
                new(halfWidth, halfHeight, 0),
                new(-halfWidth, halfHeight, 0)
            },
            TextureCoordinates = new PointCollection
            {
                new(0, 1),
                new(1, 1),
                new(1, 0),
                new(0, 0)
            },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
        };

        ImageBrush coverBrush = new(image)
        {
            Stretch = Stretch.UniformToFill,
            Opacity = slot.Opacity
        };

        MaterialGroup material = new();
        material.Children.Add(new DiffuseMaterial(coverBrush));
        material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(34, 255, 255, 255))));
        MaterialGroup backMaterial = new();
        backMaterial.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(18, 22, 28))));
        backMaterial.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(12, 255, 255, 255))));

        GeometryModel3D coverModel = new()
        {
            Geometry = mesh,
            Material = material,
            BackMaterial = backMaterial
        };

        double pivotShift = Math.Abs(slot.Rotation) < 0.01
            ? 0
            : (slot.Rotation > 0 ? -halfWidth : halfWidth);

        Transform3DGroup transforms = new();
        transforms.Children.Add(new TranslateTransform3D(pivotShift, 0, 0));
        transforms.Children.Add(new ScaleTransform3D(slot.Scale, slot.Scale, 1));
        transforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), slot.Rotation)));
        transforms.Children.Add(new TranslateTransform3D(slot.X, slot.Y, slot.Z));
        coverModel.Transform = transforms;

        Model3DGroup group = new();
        group.Children.Add(coverModel);

        MeshGeometry3D edgeMesh = new()
        {
            Positions = new Point3DCollection
            {
                new(slot.Rotation > 0 ? halfWidth : -halfWidth - 4, -halfHeight, 0),
                new(slot.Rotation > 0 ? halfWidth + 4 : -halfWidth, -halfHeight, 0),
                new(slot.Rotation > 0 ? halfWidth + 4 : -halfWidth, halfHeight, 0),
                new(slot.Rotation > 0 ? halfWidth : -halfWidth - 4, halfHeight, 0)
            },
            TextureCoordinates = new PointCollection
            {
                new(0, 0),
                new(1, 0),
                new(1, 1),
                new(0, 1)
            },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
        };

        MaterialGroup edgeMaterial = new();
        edgeMaterial.Children.Add(new DiffuseMaterial(new LinearGradientBrush(
            Color.FromArgb(150, 10, 12, 16),
            Color.FromArgb(36, 110, 120, 132),
            new Point(0, 0),
            new Point(1, 0))));

        GeometryModel3D edgeModel = new()
        {
            Geometry = edgeMesh,
            Material = edgeMaterial,
            BackMaterial = edgeMaterial
        };

        Transform3DGroup edgeTransforms = new();
        edgeTransforms.Children.Add(new TranslateTransform3D(pivotShift, 0, 0));
        edgeTransforms.Children.Add(new ScaleTransform3D(slot.Scale, slot.Scale, 1));
        edgeTransforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), slot.Rotation)));
        edgeTransforms.Children.Add(new TranslateTransform3D(slot.X, slot.Y, slot.Z));
        edgeModel.Transform = edgeTransforms;
        group.Children.Add(edgeModel);

        CoverFlowViewport.Children.Add(new ModelVisual3D { Content = group });
    }

    private static (double X, double Y, double Z, double Size, double Scale, double Rotation, double Opacity, int ZIndex, double Brightness) CreateCenterCoverFlowSlot()
    {
        return (0, 0, 0, CenterCoverSize, 1.0, 0, 1.0, 100, 1.0);
    }

    private static (double X, double Y, double Z, double Size, double Scale, double Rotation, double Opacity, int ZIndex, double Brightness) CreateCoverFlowSlot(int offset)
    {
        int distance = Math.Clamp(Math.Abs(offset), 1, 4);
        const double itemSize = CenterCoverSize;
        const double tilt = 0.91;
        const double spacing = 0.095;

        double clampedOffset = Math.Max(-1.0, Math.Min(1.0, offset));
        double x = (clampedOffset * 0.5 * tilt + offset * spacing) * itemSize;
        double z = Math.Abs(clampedOffset) * -itemSize * 0.45;
        double rotation = -clampedOffset * 90.0 * tilt;
        double scale = 1.0;
        double y = GetBottomAlignedY(itemSize, scale, z);
        int zIndex = 100 - distance * 10;
        return (x, y, z, itemSize, scale, rotation, 1.0, zIndex, 1.0);
    }

    private static double GetBottomAlignedY(double size, double scale, double z)
    {
        const double cameraZ = 1050.0;
        double perspectiveScale = cameraZ / (cameraZ - z);
        double baseline = CenterCoverSize / 2.0;
        double projectedHeight = size * scale * perspectiveScale;
        return baseline - projectedHeight / 2.0;
    }

    private static BitmapSource CreateSquareCoverSource(byte[] coverBytes)
    {
        BitmapImage source = new();
        using MemoryStream stream = new(coverBytes);
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.StreamSource = stream;
        source.EndInit();
        source.Freeze();
        return RenderSquareImage(source);
    }

    private static BitmapSource CreatePlaceholderCover(string title, string artist)
    {
        const int size = 256;
        DrawingVisual visual = new();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(28, 34, 42), Color.FromRgb(74, 86, 104), 45), null, new Rect(0, 0, size, size));
            DrawCoverText(dc, title, artist);
        }

        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource RenderSquareImage(BitmapSource source)
    {
        const int size = 256;
        double side = Math.Min(source.PixelWidth, source.PixelHeight);
        double scale = size / side;
        double drawWidth = source.PixelWidth * scale;
        double drawHeight = source.PixelHeight * scale;
        double offsetX = (size - drawWidth) / 2.0;
        double offsetY = (size - drawHeight) / 2.0;

        DrawingVisual visual = new();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, size, size));
            dc.DrawImage(source, new Rect(offsetX, offsetY, drawWidth, drawHeight));
        }

        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawCoverText(DrawingContext dc, string title, string artist)
    {
        var titleText = new FormattedText(
            title,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            46,
            Brushes.White,
            1.0);
        var artistText = new FormattedText(
            artist,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            24,
            new SolidColorBrush(Color.FromRgb(220, 230, 240)),
            1.0);

        dc.DrawText(titleText, new Point(24, 130));
        dc.DrawText(artistText, new Point(24, 190));
    }

    public void SetLyrics(string? lyrics)
    {
        if (!CurrentSettings.EnableLyrics || string.IsNullOrWhiteSpace(lyrics))
        {
            LyricsContainer.Visibility = Visibility.Collapsed;
            LyricsText.Text = string.Empty;
        }
        else
        {
            LyricsText.Text = lyrics;
            LyricsContainer.Visibility = Visibility.Visible;
        }
    }

    private void OverlayWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GwlExstyle);
        SetWindowLong(hwnd, GwlExstyle, exStyle | WsExLayered | WsExTransparent | WsExToolwindow);
    }

    public void UpdateCover(byte[]? coverBytes)
    {
        SetCover(coverBytes);
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


