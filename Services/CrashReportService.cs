using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace HorizonRadioOverlay.Services;

public static class CrashReportService
{
    private static readonly object Gate = new();
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HorizonRadioOverlay",
        "crash.log");

    private static int _initialized;
    private static string _playbackContext = "playback=<not-captured>";

    public static void Initialize(Application application)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Exception exception = args.ExceptionObject as Exception ??
                new InvalidOperationException(args.ExceptionObject?.ToString() ?? "Unknown fatal exception");
            Write("AppDomain", exception, args.IsTerminating);
        };

        application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Write("TaskScheduler", args.Exception, terminating: false);
            args.SetObserved();
        };
    }

    public static void UpdatePlaybackSnapshot(PlaybackSnapshot snapshot)
    {
        string track = snapshot.Track == null
            ? "<none>"
            : $"{snapshot.Track.Name} / {snapshot.Track.Artist}";
        string position = snapshot.Position?.TotalSeconds.ToString("F3") ?? "<none>";
        lock (Gate)
        {
            _playbackContext =
                $"source={snapshot.SourceId}; health={snapshot.Health}; " +
                $"failures={snapshot.ConsecutiveFailures}; track={track}; " +
                $"positionSeconds={position}; playing={snapshot.IsPlaying?.ToString() ?? "<none>"}; " +
                $"trackVersion={snapshot.TrackVersion}; timelineVersion={snapshot.TimelineVersion}; " +
                $"capturedAt={snapshot.CapturedAt:O}; error={snapshot.LastError ?? "<none>"}";
        }
    }

    public static void WriteStartupFailure(Exception exception)
    {
        Write("Startup", exception, terminating: true);
    }

    internal static bool IsRecoverableUiException(Exception exception)
    {
        Exception root = exception.GetBaseException();
        return root is OperationCanceledException or
            IOException or
            HttpRequestException or
            UnauthorizedAccessException or
            OverflowException;
    }

    internal static string FormatReport(
        string origin,
        Exception exception,
        bool terminating,
        string playbackContext,
        DateTimeOffset now)
    {
        StringBuilder builder = new();
        builder.AppendLine(new string('=', 72));
        builder.AppendLine($"Time: {now:O}");
        builder.AppendLine($"Origin: {origin}");
        builder.AppendLine($"Terminating: {terminating}");
        builder.AppendLine($"Type: {exception.GetType().FullName}");
        builder.AppendLine($"Playback: {playbackContext}");
        builder.AppendLine("Exception:");
        builder.AppendLine(exception.ToString());
        return builder.ToString();
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs args)
    {
        bool recoverable = IsRecoverableUiException(args.Exception);
        Write("WPF.Dispatcher", args.Exception, terminating: !recoverable);
        if (recoverable)
        {
            args.Handled = true;
        }
    }

    private static void Write(string origin, Exception exception, bool terminating)
    {
        try
        {
            string playbackContext;
            lock (Gate)
            {
                playbackContext = _playbackContext;
            }

            string report = FormatReport(
                origin,
                exception,
                terminating,
                playbackContext,
                DateTimeOffset.Now);
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            lock (Gate)
            {
                File.AppendAllText(CrashLogPath, report, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
