using System;
using System.IO;
using System.Reflection;
using Microsoft.UI.Xaml;

namespace HorizonRadioOverlay;

public partial class App : Application
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HorizonRadioOverlay");
    private static readonly string LogFile = Path.Combine(LogDir, "crash.log");

    private Window? _window;

    public App()
    {
        this.InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        WriteLog($"[Startup] v{Assembly.GetExecutingAssembly().GetName().Version} OK");

        try
        {
            bool startMinimized = args.Arguments.Contains("--autostart", StringComparison.OrdinalIgnoreCase);

            _window = new MainWindow(startMinimized);
            _window.Activate();
        }
        catch (Exception ex)
        {
            WriteCrash(ex.ToString());
            throw;
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteCrash($"[UnhandledException] {e.Exception}");
        e.Handled = true;
    }

    private static void WriteCrash(string text)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(LogFile, $"Time: {DateTime.Now}\r\n{text}\r\n");
        }
        catch { }

        try
        {
            string local = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.WriteAllText(local, $"Time: {DateTime.Now}\r\n{text}\r\n");
        }
        catch { }
    }

    private static void WriteLog(string text)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(LogFile, $"Time: {DateTime.Now} {text}\r\n");
        }
        catch { }
    }
}
