using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace HorizonRadioOverlay;

public partial class App : Application
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HorizonRadioOverlay");
    private static readonly string LogFile = Path.Combine(LogDir, "crash.log");

    private MainWindow? _mainWindow;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrash(
            $"[UnhandledException] Terminating={args.IsTerminating}\r\n{args.ExceptionObject}");
        DispatcherUnhandledException += (_, args) => WriteCrash(
            $"[DispatcherUnhandledException]\r\n{args.Exception}");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        WriteLog($"[Startup] v{Assembly.GetExecutingAssembly().GetName().Version} OK");

        try
        {
            _mainWindow = new MainWindow();
            MainWindow = _mainWindow;
            _mainWindow.Show();
        }
        catch (Exception ex)
        {
            WriteCrash(ex.ToString());
            throw;
        }
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
