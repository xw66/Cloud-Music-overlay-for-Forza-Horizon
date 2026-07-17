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
    private async Task SilentCheckUpdateAsync()
    {
        try
        {
            UpdateInfo info = await _updateService.CheckForUpdateAsync();
            if (info.HasUpdate && string.IsNullOrEmpty(info.ErrorMessage))
            {
                SetStatus($"状态：发现新版本 {info.LatestVersion}，点击“检查更新”查看。", false);
            }
        }
        catch
        {
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("状态：正在检查更新…", false);
        UpdateInfo info = await _updateService.CheckForUpdateAsync();

        if (!string.IsNullOrEmpty(info.ErrorMessage))
        {
            SetStatus($"状态：{info.ErrorMessage}", true);
            return;
        }

        if (!info.HasUpdate)
        {
            string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            SetStatus($"状态：已是最新版本（v{ver}）。", false);
            return;
        }

        string msg = $"发现新版本 {info.LatestVersion}！\n\n";
        if (!string.IsNullOrEmpty(info.ReleaseNotes))
        {
            msg += $"更新内容：\n{info.ReleaseNotes}\n\n";
        }
        msg += "是否前往 GitHub 下载？";

        var result = System.Windows.MessageBox.Show(msg, "发现更新",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = info.DownloadUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                SetStatus("状态：无法打开下载链接。", true);
            }
        }
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/xw66/Cloud-Music-overlay-for-Forza-Horizon",
                    UseShellExecute = true
                });
        }
        catch
        {
            SetStatus("状态：无法打开浏览器。", true);
        }
    }

    private void BilibiliLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch
        {
            SetStatus("状态：无法打开哔哩哔哩个人空间。", false);
        }
    }

    private void BaiduPanButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://pan.baidu.com/s/1bWOQYrgYVhHSWJTunLNhBA?pwd=6666",
                    UseShellExecute = true
                });
        }
        catch
        {
            SetStatus("状态：无法打开浏览器。", true);
        }
    }
}
