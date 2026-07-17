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
    private Task<RemoteControlStatus> GetRemoteControlStatusAsync()
    {
        return Dispatcher.InvokeAsync(() =>
        {
            string title = _nowPlayingPageView?.CurrentTitle.Text ?? string.Empty;
            string artist = _nowPlayingPageView?.CurrentArtist.Text ?? string.Empty;
            return new RemoteControlStatus
            {
                Title = title,
                Artist = artist,
                Source = _playbackCoordinator.GetSource(_activeSettings.TrackSource).DisplayName,
                SupportsSmtc = _playbackCoordinator.SupportsSource(PlaybackSourceIds.Smtc),
                OverlayVisible = _overlayWindow.IsContentVisible,
                RemoteEnabled = _activeSettings.EnableRemoteControl
            };
        }).Task;
    }

    private Task HandleRemoteControlActionAsync(string action)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            switch (action)
            {
                case "previous":
                    await PrevAsync();
                    break;
                case "next":
                    await NextAsync();
                    break;
                case "togglePlayPause":
                    await TogglePlayPauseAsync();
                    break;
                case "toggleOverlay":
                    await ToggleOverlayVisibilityAsync();
                    break;
            }
        }).Task.Unwrap();
    }

    private string GetRemoteDisplayUrl()
    {
        return _activeSettings.RemoteControlAllowLan ? _remoteControlService.LanUrl : _remoteControlService.LocalUrl;
    }

    private void UpdateRemoteControlPage()
    {
        if (_remoteControlPageView == null)
        {
            return;
        }

        string address = GetRemoteDisplayUrl();
        _remoteControlPageView.RemoteControlStatusText.Text = _activeSettings.EnableRemoteControl
            ? _remoteControlService.StatusMessage
            : "未启用";
        _remoteControlPageView.RemoteControlAddressBox.Text = _activeSettings.EnableRemoteControl ? address : string.Empty;
        _remoteControlPageView.RemoteControlHelpText.Text = BuildRemoteControlHelpText();
        _remoteControlPageView.SetQrCode(_activeSettings.EnableRemoteControl ? address : string.Empty);
    }

    private string BuildRemoteControlHelpText()
    {
        if (!_activeSettings.EnableRemoteControl)
        {
            return "启用后会生成手机访问地址和二维码。";
        }

        if (!_remoteControlService.IsRunning)
        {
            return "服务未运行。请检查端口是否被占用，或换一个端口后重新启用。";
        }

        if (!_activeSettings.RemoteControlAllowLan)
        {
            return "当前只允许本机访问。若要用手机连接，请勾选允许局域网手机访问。";
        }

        IReadOnlyList<string> urls = _remoteControlService.LanUrls;
        if (urls.Count == 0)
        {
            return "未检测到可用局域网 IPv4。请确认电脑已连接 Wi-Fi/网线，且网络不是仅本机或虚拟网卡。";
        }

        if (urls.Count == 1)
        {
            return "手机打不开时，请确认手机和电脑在同一 Wi-Fi，并允许 Windows 防火墙放行本程序。";
        }

        return "备用地址：" + string.Join("  ", urls.Skip(1)) + "。手机打不开首选地址时可尝试备用地址。";
    }
}
