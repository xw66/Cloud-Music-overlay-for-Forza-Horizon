using System.Runtime.Versioning;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.ViewModels;

[SupportedOSPlatform("windows")]
public sealed partial class NowPlayingViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = UiText.NoTrackDetected;

    [ObservableProperty]
    private string _artist = UiText.PleasePlayMedia;

    [ObservableProperty]
    private string _sourceText = UiText.SourceUnknown;

    [ObservableProperty]
    private BitmapImage? _coverImage;

    [ObservableProperty]
    private string _lyricsPreview = string.Empty;

    [ObservableProperty]
    private string _connectionStatus = "正在连接播放器";

    [ObservableProperty]
    private string _connectionStatusDetail = "等待首个播放器数据快照。";

    public IAsyncRelayCommand? PrevCommand { get; set; }
    public IAsyncRelayCommand? PlayPauseCommand { get; set; }
    public IAsyncRelayCommand? NextCommand { get; set; }
    public IAsyncRelayCommand? RefreshCommand { get; set; }
    public IRelayCommand<string>? NavigateCommand { get; set; }

    public void ResetTrack()
    {
        Title = UiText.NoTrackDetected;
        Artist = UiText.PleasePlayMedia;
        SourceText = UiText.SourceUnknown;
        CoverImage = null;
        LyricsPreview = string.Empty;
    }
}
