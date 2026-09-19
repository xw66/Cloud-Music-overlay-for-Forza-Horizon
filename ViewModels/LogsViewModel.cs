using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.ViewModels;

[SupportedOSPlatform("windows")]
public sealed partial class LogsViewModel : ObservableObject
{
    private readonly DiagnosticService? _diagnostic;

    [ObservableProperty]
    private string _logText = string.Empty;

    public event Action<string, bool>? StatusNotification;
    public event Action? RequestScrollToEnd;

    public LogsViewModel(DiagnosticService? diagnostic = null)
    {
        _diagnostic = diagnostic;
    }

    [RelayCommand]
    public void Refresh()
    {
        if (_diagnostic == null)
        {
            return;
        }

        try
        {
            LogText = _diagnostic.ReadCurrentLogText();
            RequestScrollToEnd?.Invoke();
        }
        catch
        {
        }
    }

    [RelayCommand]
    private void OpenLogFile()
    {
        if (_diagnostic == null)
        {
            return;
        }

        try
        {
            _diagnostic.Event("打开日志文件。");
            Process.Start(new ProcessStartInfo
            {
                FileName = _diagnostic.LogFilePath,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    [RelayCommand]
    private void CopyLog()
    {
        try
        {
            Clipboard.SetText(LogText ?? string.Empty);
            _diagnostic?.Event("复制日志内容。");
            StatusNotification?.Invoke("状态：日志已复制。", true);
        }
        catch
        {
            StatusNotification?.Invoke("状态：复制日志失败。", false);
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        if (_diagnostic == null)
        {
            return;
        }

        try
        {
            _diagnostic.Clear();
            Refresh();
            StatusNotification?.Invoke("状态：日志已清空。", true);
        }
        catch
        {
            StatusNotification?.Invoke("状态：清空日志失败。", false);
        }
    }
}
