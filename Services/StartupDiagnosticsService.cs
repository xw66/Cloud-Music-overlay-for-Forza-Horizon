using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public static class StartupDiagnosticsService
{
    public static void LogSnapshot(DiagnosticService diagnostic, OverlaySettings settings)
    {
        string traceId = "STARTUP";
        diagnostic.Event(DiagnosticContext.Format(traceId, "startup", "environment",
            ("version", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "<unknown>"),
            ("os", RuntimeInformation.OSDescription),
            ("arch", RuntimeInformation.ProcessArchitecture),
            ("framework", RuntimeInformation.FrameworkDescription),
            ("source", settings.TrackSource),
            ("diagnosticMode", settings.DiagnosticMode),
            ("enableLyrics", settings.EnableLyrics)));

        diagnostic.Event(DiagnosticContext.Format(traceId, "startup", "network",
            ("httpProxy", Environment.GetEnvironmentVariable("HTTP_PROXY")),
            ("httpsProxy", Environment.GetEnvironmentVariable("HTTPS_PROXY")),
            ("noProxy", Environment.GetEnvironmentVariable("NO_PROXY"))));

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string logPath = diagnostic.LogFilePath;
        string coverDir = Path.Combine(appData, "HorizonRadioOverlay", "covers");
        diagnostic.Event(DiagnosticContext.Format(traceId, "startup", "storage",
            ("logPath", logPath), ("logWritable", CanWriteTo(Path.GetDirectoryName(logPath))),
            ("coverDir", coverDir), ("coverWritable", CanWriteTo(coverDir))));

        string[] dataDirs =
        [
            Path.Combine(appData, "Netease", "CloudMusic"),
            Path.Combine(appData, "Netease", "cloudmusic"),
            Path.Combine(appData, "NetEase Music"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Netease", "CloudMusic"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming", "Netease", "CloudMusic")
        ];

        diagnostic.Event(DiagnosticContext.Format(traceId, "startup", "netease-data-dirs",
            ("dirs", string.Join(" | ", dataDirs.Select(x => $"{(Directory.Exists(x) ? "exists" : "missing")}:{x}")))));
    }

    private static bool CanWriteTo(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
