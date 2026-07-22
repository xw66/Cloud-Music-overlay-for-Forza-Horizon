using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;
using HorizonRadioOverlay.ViewModels;
using HorizonRadioOverlay.Views.Pages;

namespace HorizonRadioOverlay;

public partial class MainWindow
{
    private void LoadEmbeddedResources()
    {
        try
        {
            var iconUri = new Uri("pack://application:,,,/icon.ico");
            var iconStream = Application.GetResourceStream(iconUri)?.Stream;
            if (iconStream != null)
            {
                var decoder = BitmapDecoder.Create(
                    iconStream,
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
                Icon = decoder.Frames[0];
            }
        }
        catch { }

        try
        {
            var imgUri = new Uri("pack://application:,,,/Assets/Icons/icons8-github-50.png");
            var imgStream = Application.GetResourceStream(imgUri)?.Stream;
            if (imgStream != null && GitHubImage != null)
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = imgStream;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                GitHubImage.Source = img;
            }
        }
        catch { }
    }

    private void InitializeTrayIcon()
    {
        if (!RuntimeFeatureSupport.SupportsTrayIcon())
        {
            _trayIcon = null;
            return;
        }

        System.Drawing.Icon? icon = null;
        try
        {
            var uri = new Uri("pack://application:,,,/icon.ico");
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo?.Stream != null)
            {
                icon = new System.Drawing.Icon(streamInfo.Stream);
            }
        }
        catch
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath))
            {
                try { icon = new System.Drawing.Icon(iconPath); } catch { }
            }
        }

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "网易云悬浮窗 v3.0.1",
            Visible = false
        };

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("显示主窗口", null, (_, _) => RestoreFromTray());
        contextMenu.Items.Add("退出", null, (_, _) => RealExit());
        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    internal void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
        }
    }

    private void RealExit()
    {
        _isRealExit = true;
        Close();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isRealExit || !_activeSettings.MinimizeToTray || _trayIcon == null)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = true;
            _trayIcon.ShowBalloonTip(2000, "网易云悬浮窗 v3.0.1", "已最小化到托盘，双击图标恢复。", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (_startupInitialized)
        {
            return;
        }

        _startupInitialized = true;
        _gamepadInputService.AttachRawInput(this, _diagnostic);
        bool ok = RebindGlobalHotkeys();
        SetStatus(ok
            ? "状态：快捷键已就绪，按应用快捷键会转发网易云快捷键。"
            : "状态：应用快捷键注册失败（被占用或格式无效）。", false);

        _lifecycle.StartAll();
        _ = RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
        Dispatcher.BeginInvoke(async () => await SilentCheckUpdateAsync(), DispatcherPriority.Background);

        if (_startHiddenToTray)
        {
            BeginInvokeHideToTray();
        }
    }

    public void PrepareAutoStartToTray()
    {
        ShowInTaskbar = false;
        WindowState = WindowState.Minimized;
    }

    private void BeginInvokeHideToTray()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
            Hide();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = true;
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _pageMetaUpdateTimer.Stop();
        _lifecycle.Dispose();
    }
}
