using System;
using System.IO;
using System.Windows;

namespace HorizonRadioOverlay;

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
}
