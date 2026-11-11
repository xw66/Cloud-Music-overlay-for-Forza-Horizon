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
            string title = _nowPlayingViewModel?.Title ?? string.Empty;
            string artist = _nowPlayingViewModel?.Artist ?? string.Empty;
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

    private Task<PlaybackCommandResult> HandleRemoteControlActionAsync(string action)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            switch (action)
            {
                case "previous":
                    return await PrevAsync();
                case "next":
                    return await NextAsync();
                case "togglePlayPause":
                    return await TogglePlayPauseAsync();
                case "toggleOverlay":
                    return await ToggleOverlayVisibilityAsync();
                default:
                    return new PlaybackCommandResult(PlaybackCommandStatus.Unsupported, _activeSettings.TrackSource);
            }
        }).Task.Unwrap();
    }

    private void UpdateRemoteControlPage()
    {
        _remoteControlViewModel.UpdateRuntimeStatus(_remoteControlService, _activeSettings);
    }
}
