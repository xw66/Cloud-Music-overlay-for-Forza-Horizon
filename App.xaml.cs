using System.IO;
using System.Runtime.Versioning;
using System.Windows;

using System.Windows.Media;
using System.Windows.Media.Imaging;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay;

[SupportedOSPlatform("windows")]
public partial class App : Application
{
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            string log = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HorizonRadioOverlay", "crash.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.WriteAllText(log,
                    $"Time: {DateTime.Now}\r\n" +
                    $"Type: {args.ExceptionObject.GetType()}\r\n" +
                    $"Exception: {args.ExceptionObject}\r\n" +
                    $"Terminating: {args.IsTerminating}\r\n");
            }
            catch { }
        };

        try
        {
            base.OnStartup(e);

            if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--coverflow-test-shot", StringComparison.OrdinalIgnoreCase))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ = RunCoverFlowScreenshotAsync(e.Args[1]);
                return;
            }

            _mainWindow = new MainWindow();
            MainWindow = _mainWindow;
            _mainWindow.Show();
        }
        catch (Exception ex)
        {
            string log = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HorizonRadioOverlay", "crash.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.WriteAllText(log,
                    $"Time: {DateTime.Now}\r\n" +
                    $"Type: {ex.GetType()}\r\n" +
                    $"Exception: {ex}\r\n");
            }
            catch { }
            throw;
        }
    }

    private async Task RunCoverFlowScreenshotAsync(string outputPath)
    {
        try
        {
            OverlayWindow overlay = new();
            overlay.ApplySettings(new OverlaySettings
            {
                EnableCoverWingEffect = true,
                AlwaysShowOverlay = true,
                LeftPercent = 0.0,
                TopPercent = 0.0,
                Scale = 1.0
            });

            (string Name, string Artist, Color Color)[] tracks =
            [
                ("有些", "颜人中", Color.FromRgb(39, 94, 113)),
                ("风景", "测试歌手", Color.FromRgb(112, 58, 72)),
                ("夜航", "测试歌手", Color.FromRgb(58, 86, 131)),
                ("岛屿", "测试歌手", Color.FromRgb(82, 112, 70)),
                ("回声", "测试歌手", Color.FromRgb(130, 88, 48))
            ];

            foreach ((string name, string artist, Color color) in tracks)
            {
                await overlay.ShowTrackAsync(new TrackInfo
                {
                    Name = name,
                    Artist = artist,
                    SourceAppId = "CoverFlowTest",
                    CoverBytes = CreatePreviewCoverBytes(name, artist, color)
                });
                await Task.Delay(700);
            }

            await Task.Delay(900);
            overlay.UpdateLayout();

            int width = Math.Max(1, (int)Math.Ceiling(overlay.Width));
            int height = Math.Max(1, (int)Math.Ceiling(overlay.Height));
            RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(overlay);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(outputPath);
            encoder.Save(stream);

            overlay.Close();
        }
        finally
        {
            Shutdown();
        }
    }

    private static byte[] CreatePreviewCoverBytes(string title, string artist, Color accent)
    {
        const int size = 256;
        DrawingVisual visual = new();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(
                Color.FromRgb(16, 20, 24),
                accent,
                new Point(0, 0),
                new Point(1, 1));
            dc.DrawRectangle(background, null, new Rect(0, 0, size, size));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(58, 255, 255, 255)), null, new Point(56, 46), 86, 54);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(44, 0, 0, 0)), null, new Point(204, 196), 92, 82);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(58, 0, 0, 0)), null, new Rect(0, 168, size, 88));

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

        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
