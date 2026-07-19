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
    private void InitializeOverlayControls(OverlaySettings settings)
    {
        _isInitializingOverlayControls = true;
        try
        {
            if (_floatingSettingsPageView != null)
            {
                InitializeTrackSourceOptions();
                SelectTrackSource(settings.TrackSource);
                _floatingSettingsPageView.HorizontalSlider.Value = settings.LeftPercent * 100.0;
                _floatingSettingsPageView.BottomOffsetSlider.Value = settings.TopPercent * 100.0;
                _floatingSettingsPageView.ScaleSlider.Value = settings.Scale * 100.0;
                _floatingSettingsPageView.MinimizeToTrayCheckBox.IsChecked = settings.MinimizeToTray;
                _floatingSettingsPageView.AutoStartCheckBox.IsChecked = settings.AutoStartOnBoot;
                _floatingSettingsPageView.AlwaysShowCheckBox.IsChecked = settings.AlwaysShowOverlay;
                _floatingSettingsPageView.HideOverlayWhenPausedCheckBox.IsChecked = settings.HideOverlayWhenPaused;
                _floatingSettingsPageView.DiagnosticCheckBox.IsChecked = settings.DiagnosticMode;
                _floatingSettingsPageView.EnableLyricsCheckBox.IsChecked = settings.EnableLyrics;
                _floatingSettingsPageView.EnableNeteaseMemoryTimelineCheckBox.IsChecked = settings.EnableNeteaseMemoryTimeline;
                _floatingSettingsPageView.EnableCoverWingEffectCheckBox.IsChecked = settings.EnableCoverWingEffect;
            }

            if (_hotkeySettingsPageView != null)
            {
                _hotkeySettingsPageView.AppPrevHotkeyBox.Text = settings.AppPrevHotkey;
                _hotkeySettingsPageView.AppNextHotkeyBox.Text = settings.AppNextHotkey;
                _hotkeySettingsPageView.AppToggleHotkeyBox.Text = settings.AppToggleHotkey;
                _hotkeySettingsPageView.AppToggleOverlayHotkeyBox.Text = settings.AppToggleOverlayHotkey;
                _hotkeySettingsPageView.NeteasePrevHotkeyBox.Text = settings.NeteasePrevHotkey;
                _hotkeySettingsPageView.NeteaseNextHotkeyBox.Text = settings.NeteaseNextHotkey;
                _hotkeySettingsPageView.NeteaseToggleHotkeyBox.Text = settings.NeteaseToggleHotkey;
                _hotkeySettingsPageView.EnableGamepadCheckBox.IsChecked = settings.EnableGamepadHotkeys;
                _hotkeySettingsPageView.GamepadPrevHotkeyBox.Text = settings.GamepadPrevHotkey;
                _hotkeySettingsPageView.GamepadNextHotkeyBox.Text = settings.GamepadNextHotkey;
                _hotkeySettingsPageView.GamepadToggleHotkeyBox.Text = settings.GamepadToggleHotkey;
                _hotkeySettingsPageView.GamepadToggleOverlayHotkeyBox.Text = settings.GamepadToggleOverlayHotkey;
            }

            if (_remoteControlPageView != null)
            {
                _remoteControlPageView.EnableRemoteControlCheckBox.IsChecked = settings.EnableRemoteControl;
                _remoteControlPageView.RemoteControlPortBox.Text = settings.RemoteControlPort.ToString();
                _remoteControlPageView.RemoteControlAllowLanCheckBox.IsChecked = settings.RemoteControlAllowLan;
            }

            if (_themeSettingsPageView != null)
            {
                SelectTitleColor(settings.TitleColor);
                SelectArtistColor(settings.ArtistColor);
                SelectLyricsColor(settings.LyricsColor);
                _themeSettingsPageView.TitleOpacitySlider.Value = settings.TitleOpacity * 100.0;
                _themeSettingsPageView.ArtistOpacitySlider.Value = settings.ArtistOpacity * 100.0;
                _themeSettingsPageView.LyricsOpacitySlider.Value = settings.LyricsOpacity * 100.0;
            }

            ApplyDisplayColors(settings);
            UpdateOverlayControlLabels();
            UpdateRuntimeDependentControls();
            UpdateRemoteControlPage();
        }
        finally
        {
            _isInitializingOverlayControls = false;
        }
    }

    private void HorizontalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls)
        {
            return;
        }

        ApplyOverlaySettingsFromControls();
    }

    private void BottomOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls)
        {
            return;
        }

        ApplyOverlaySettingsFromControls();
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls)
        {
            return;
        }

        ApplyOverlaySettingsFromControls();
    }

    private async void TrackSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingOverlayControls)
        {
            return;
        }

        ApplyOverlaySettingsFromControls();
        UpdateRuntimeDependentControls();
        _smtcCoverRefreshCts?.Cancel();
        _smtcCoverRefreshCts = null;
        _lyricsService.Reset();
        _lastSmtcPlaybackPositionSeconds = null;
        _smtcLyricTimingController.Reset();
        _neteaseLyricTimingController.Reset();
        _lastTrackKey = string.Empty;
        _lastDisplayTrackKey = string.Empty;
        _lastPreviewCoverBytes = null;
        SetCover(null);
        _overlayWindow.SetLyrics(null);

        await RefreshCurrentTrackAsync(showOverlay: false, allowOverlayOnTrackChange: false);
        SetStatus("状态：数据来源已切换（点击“保存”可持久化）。", false);
    }

    private void SaveOverlaySettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyOverlaySettingsFromControls();

            if (!AreHotkeysValid(_activeSettings))
            {
                SetStatus("状态：快捷键格式无效，示例 Ctrl+Shift+Left。", true);
                return;
            }

            bool rebound = RebindGlobalHotkeys();
            if (!rebound)
            {
                SetStatus("状态：应用快捷键注册失败，请换一组按键后再保存。", true);
                return;
            }

            _overlaySettingsService.Save(_activeSettings);
            _remoteControlService.ApplySettings(_activeSettings);
            UpdateRemoteControlPage();
            SetStatus($"状态：已保存并应用。{_overlaySettingsService.SettingsFilePath}", false);
        }
        catch (Exception ex)
        {
            SetStatus($"状态：保存失败。{ex.Message}", true);
        }
    }

    private async void SettingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
        _diagnostic.Enabled = _activeSettings.DiagnosticMode;
        _overlaySettingsService.Save(_activeSettings);
        _remoteControlService.ApplySettings(_activeSettings);
        UpdateRemoteControlPage();
        await ApplyPauseOverlayVisibilityRuleAsync();
    }

    private void RemoteControlSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
        _overlaySettingsService.Save(_activeSettings);
        _remoteControlService.ApplySettings(_activeSettings);
        UpdateRemoteControlPage();
        SetStatus(_remoteControlService.IsRunning ? "状态：手机遥控已应用。" : $"状态：{_remoteControlService.StatusMessage}", !_remoteControlService.IsRunning && _activeSettings.EnableRemoteControl);
    }

    private void CopyRemoteAddress_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string address = _remoteControlPageView?.RemoteControlAddressBox.Text ?? GetRemoteDisplayUrl();
            if (string.IsNullOrWhiteSpace(address))
            {
                SetStatus("状态：手机遥控地址为空，请先启用遥控服务。", true);
                return;
            }

            Clipboard.SetText(address);
            SetStatus("状态：手机遥控地址已复制。", false);
        }
        catch (Exception ex)
        {
            SetStatus($"状态：复制手机遥控地址失败。{ex.Message}", true);
        }
    }

    private void ResetRemoteToken_Click(object sender, RoutedEventArgs e)
    {
        _activeSettings.RemoteControlToken = RemoteControlService.GenerateToken();
        _overlaySettingsService.Save(_activeSettings);
        _remoteControlService.ApplySettings(_activeSettings);
        UpdateRemoteControlPage();
        SetStatus("状态：手机遥控连接令牌已重置。", false);
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
        if (!AreHotkeysValid(_activeSettings))
        {
            SetStatus("状态：快捷键格式无效，示例 Ctrl+Shift+H 或 Back+Start。", true);
            return;
        }

        if (!RebindGlobalHotkeys())
        {
            SetStatus("状态：应用快捷键注册失败，请换一组按键。", true);
            return;
        }

        _overlaySettingsService.Save(_activeSettings);
        SetStatus("状态：快捷键已应用。", false);
    }

    private void ResetOverlaySettings_Click(object sender, RoutedEventArgs e)
    {
        _activeSettings = new OverlaySettings();
        _smtcLyricTimingController.SetDelayOverrideMilliseconds(_activeSettings.SmtcLyricDelayOverrideMs);
        _overlayWindow.ApplySettings(_activeSettings);
        _remoteControlService.ApplySettings(_activeSettings);
        InitializeOverlayControls(_activeSettings);
        SetStatus("状态：已重置为默认值，点击“保存”后生效并持久化。", false);
    }

    private async void TestOverlay_Click(object sender, RoutedEventArgs e)
    {
        (string Name, string Artist, Color Color)[] previewTracks =
        [
            ("有些", "颜人中", Color.FromRgb(39, 94, 113)),
            ("风景", "测试歌手", Color.FromRgb(112, 58, 72)),
            ("夜航", "测试歌手", Color.FromRgb(58, 86, 131)),
            ("岛屿", "测试歌手", Color.FromRgb(82, 112, 70)),
            ("回声", "测试歌手", Color.FromRgb(130, 88, 48))
        ];

        foreach ((string name, string artist, Color color) in previewTracks)
        {
            TrackInfo previewTrack = new()
            {
                Name = name,
                Artist = artist,
                SourceAppId = "预览",
                CoverBytes = CreatePreviewCoverBytes(name, artist, color)
            };

            await _overlayWindow.ShowTrackAsync(previewTrack);
            await Task.Delay(450);
        }

        SetStatus("状态：已显示悬浮窗预览。", false);
    }

    private static byte[] CreatePreviewCoverBytes(string title, string artist, Color accent)
    {
        const int size = 256;
        DrawingVisual visual = new();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(
                Color.FromRgb(16, 20, 24),
                accent,
                new Point(0, 0),
                new Point(1, 1));
            dc.DrawRectangle(background, null, new Rect(0, 0, size, size));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(58, 255, 255, 255)), null, new Point(56, 46), 86, 54);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(44, 0, 0, 0)), null, new Point(204, 196), 92, 82);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(58, 0, 0, 0)), null, new Rect(0, 168, size, 88));

            var titleText = new FormattedText(
                title,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"),
                46,
                Brushes.White,
                1.0);
            var artistText = new FormattedText(
                artist,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"),
                24,
                new SolidColorBrush(Color.FromRgb(220, 230, 240)),
                1.0);

            dc.DrawText(titleText, new Point(24, 130));
            dc.DrawText(artistText, new Point(24, 190));
        }

        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private void SelectTrackSource(string trackSource)
    {
        if (_floatingSettingsPageView == null)
        {
            return;
        }

        foreach (object item in _floatingSettingsPageView.TrackSourceComboBox.Items)
        {
            if (item is ComboBoxItem combo &&
                combo.Tag is string tag &&
                string.Equals(tag, trackSource, StringComparison.OrdinalIgnoreCase))
            {
                _floatingSettingsPageView.TrackSourceComboBox.SelectedItem = combo;
                return;
            }
        }

        _floatingSettingsPageView.TrackSourceComboBox.SelectedIndex = 0;
    }

    private void InitializeTrackSourceOptions()
    {
        if (_floatingSettingsPageView == null)
        {
            return;
        }

        ComboBox comboBox = _floatingSettingsPageView.TrackSourceComboBox;
        comboBox.Items.Clear();
        foreach (IPlaybackSource source in _playbackCoordinator.Sources
                     .OrderBy(source => string.Equals(
                         source.Id,
                         PlaybackSourceIds.Netease,
                         StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(source => source.DisplayName, StringComparer.CurrentCulture))
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Tag = source.Id,
                Content = source.DisplayName,
                IsEnabled = source.IsAvailable,
                ToolTip = source.IsAvailable ? null : "当前系统不支持此播放器来源"
            });
        }
    }

    private void ApplyRuntimeFeatureAvailability()
    {
        if (!_playbackCoordinator.SupportsSource(PlaybackSourceIds.Smtc) &&
            PlaybackCoordinator.IsSmtcSource(_activeSettings.TrackSource))
        {
            _activeSettings.TrackSource = PlaybackSourceIds.Netease;
        }

        UpdateRuntimeDependentControls();
    }

    private void UpdateRuntimeDependentControls()
    {
        if (_floatingSettingsPageView == null)
        {
            return;
        }

        bool smtcSupported = _playbackCoordinator.SupportsSource(PlaybackSourceIds.Smtc);
        ComboBoxItem? smtcItem = null;
        foreach (object item in _floatingSettingsPageView.TrackSourceComboBox.Items)
        {
            if (item is ComboBoxItem combo &&
                string.Equals(combo.Tag as string, PlaybackSourceIds.Smtc, StringComparison.OrdinalIgnoreCase))
            {
                smtcItem = combo;
                break;
            }
        }

        if (smtcItem != null)
        {
            smtcItem.IsEnabled = smtcSupported;
            smtcItem.ToolTip = smtcSupported
                ? "独立使用系统媒体会话读取歌曲、封面、播放状态和时间轴；不会读取网易云专用渠道"
                : "当前系统版本不支持 SMTC";
        }

        if (!smtcSupported && PlaybackCoordinator.IsSmtcSource(GetSelectedTrackSource()))
        {
            SelectTrackSource(PlaybackSourceIds.Netease);
        }

        string selectedSource = GetSelectedTrackSource();
        bool useSmtc = smtcSupported && PlaybackCoordinator.IsSmtcSource(selectedSource);

        _floatingSettingsPageView.EnableLyricsCheckBox.IsEnabled =
            _playbackCoordinator.GetCapabilities(selectedSource).HasFlag(PlaybackSourceCapabilities.Lyrics);
        _floatingSettingsPageView.EnableLyricsCheckBox.ToolTip = TrackSourcePolicy.GetLyricsTooltip(useSmtc);
        _floatingSettingsPageView.EnableNeteaseMemoryTimelineCheckBox.IsEnabled = !useSmtc;
        _floatingSettingsPageView.EnableNeteaseMemoryTimelineCheckBox.ToolTip = useSmtc
            ? "该选项仅用于网易云专用渠道，SMTC 渠道不会访问网易云进程"
            : "只读扫描网易云进程时间轴，不使用 SMTC；关闭后歌词使用本地计时降级";
    }

    private string GetSelectedTrackSource()
    {
        if (_floatingSettingsPageView?.TrackSourceComboBox.SelectedItem is ComboBoxItem combo && combo.Tag is string tag)
        {
            return tag;
        }

        return PlaybackSourceIds.Netease;
    }

    private void ApplyOverlaySettingsFromControls()
    {
        OverlaySettings settings = new()
        {
            TrackSource = _activeSettings.TrackSource,
            LeftPercent = _activeSettings.LeftPercent,
            TopPercent = _activeSettings.TopPercent,
            Scale = _activeSettings.Scale,
            AppPrevHotkey = _activeSettings.AppPrevHotkey,
            AppNextHotkey = _activeSettings.AppNextHotkey,
            AppToggleHotkey = _activeSettings.AppToggleHotkey,
            AppToggleOverlayHotkey = _activeSettings.AppToggleOverlayHotkey,
            NeteasePrevHotkey = _activeSettings.NeteasePrevHotkey,
            NeteaseNextHotkey = _activeSettings.NeteaseNextHotkey,
            NeteaseToggleHotkey = _activeSettings.NeteaseToggleHotkey,
            EnableGamepadHotkeys = _activeSettings.EnableGamepadHotkeys,
            GamepadPrevHotkey = _activeSettings.GamepadPrevHotkey,
            GamepadNextHotkey = _activeSettings.GamepadNextHotkey,
            GamepadToggleHotkey = _activeSettings.GamepadToggleHotkey,
            GamepadToggleOverlayHotkey = _activeSettings.GamepadToggleOverlayHotkey,
            EnableRemoteControl = _activeSettings.EnableRemoteControl,
            RemoteControlPort = _activeSettings.RemoteControlPort,
            RemoteControlToken = _activeSettings.RemoteControlToken,
            RemoteControlAllowLan = _activeSettings.RemoteControlAllowLan,
            MinimizeToTray = _activeSettings.MinimizeToTray,
            AutoStartOnBoot = _activeSettings.AutoStartOnBoot,
            AlwaysShowOverlay = _activeSettings.AlwaysShowOverlay,
            HideOverlayWhenPaused = _activeSettings.HideOverlayWhenPaused,
            DiagnosticMode = _activeSettings.DiagnosticMode,
            EnableLyrics = _activeSettings.EnableLyrics,
            EnableNeteaseMemoryTimeline = _activeSettings.EnableNeteaseMemoryTimeline,
            EnableCoverWingEffect = _activeSettings.EnableCoverWingEffect,
            TitleColor = _activeSettings.TitleColor,
            ArtistColor = _activeSettings.ArtistColor,
            LyricsColor = _activeSettings.LyricsColor,
            TitleOpacity = _activeSettings.TitleOpacity,
            ArtistOpacity = _activeSettings.ArtistOpacity,
            LyricsOpacity = _activeSettings.LyricsOpacity,
            SmtcLyricDelayOverrideMs = _activeSettings.SmtcLyricDelayOverrideMs
        };

        if (_floatingSettingsPageView != null)
        {
            settings.TrackSource = GetSelectedTrackSource();
            settings.LeftPercent = _floatingSettingsPageView.HorizontalSlider.Value / 100.0;
            settings.TopPercent = _floatingSettingsPageView.BottomOffsetSlider.Value / 100.0;
            settings.Scale = _floatingSettingsPageView.ScaleSlider.Value / 100.0;
            settings.MinimizeToTray = _floatingSettingsPageView.MinimizeToTrayCheckBox.IsChecked == true;
            settings.AutoStartOnBoot = _floatingSettingsPageView.AutoStartCheckBox.IsChecked == true;
            settings.AlwaysShowOverlay = _floatingSettingsPageView.AlwaysShowCheckBox.IsChecked == true;
            settings.HideOverlayWhenPaused = _floatingSettingsPageView.HideOverlayWhenPausedCheckBox.IsChecked == true;
            settings.DiagnosticMode = _floatingSettingsPageView.DiagnosticCheckBox.IsChecked == true;
            settings.EnableLyrics = _floatingSettingsPageView.EnableLyricsCheckBox.IsChecked == true;
            settings.EnableNeteaseMemoryTimeline = _floatingSettingsPageView.EnableNeteaseMemoryTimelineCheckBox.IsChecked == true;
            settings.EnableCoverWingEffect = _floatingSettingsPageView.EnableCoverWingEffectCheckBox.IsChecked == true;
        }

        if (_hotkeySettingsPageView != null)
        {
            settings.AppPrevHotkey = _hotkeySettingsPageView.AppPrevHotkeyBox.Text.Trim();
            settings.AppNextHotkey = _hotkeySettingsPageView.AppNextHotkeyBox.Text.Trim();
            settings.AppToggleHotkey = _hotkeySettingsPageView.AppToggleHotkeyBox.Text.Trim();
            settings.AppToggleOverlayHotkey = _hotkeySettingsPageView.AppToggleOverlayHotkeyBox.Text.Trim();
            settings.NeteasePrevHotkey = _hotkeySettingsPageView.NeteasePrevHotkeyBox.Text.Trim();
            settings.NeteaseNextHotkey = _hotkeySettingsPageView.NeteaseNextHotkeyBox.Text.Trim();
            settings.NeteaseToggleHotkey = _hotkeySettingsPageView.NeteaseToggleHotkeyBox.Text.Trim();
            settings.EnableGamepadHotkeys = _hotkeySettingsPageView.EnableGamepadCheckBox.IsChecked == true;
            settings.GamepadPrevHotkey = _hotkeySettingsPageView.GamepadPrevHotkeyBox.Text.Trim();
            settings.GamepadNextHotkey = _hotkeySettingsPageView.GamepadNextHotkeyBox.Text.Trim();
            settings.GamepadToggleHotkey = _hotkeySettingsPageView.GamepadToggleHotkeyBox.Text.Trim();
            settings.GamepadToggleOverlayHotkey = _hotkeySettingsPageView.GamepadToggleOverlayHotkeyBox.Text.Trim();
        }

        if (_remoteControlPageView != null)
        {
            settings.EnableRemoteControl = _remoteControlPageView.EnableRemoteControlCheckBox.IsChecked == true;
            settings.RemoteControlAllowLan = _remoteControlPageView.RemoteControlAllowLanCheckBox.IsChecked == true;
            if (int.TryParse(_remoteControlPageView.RemoteControlPortBox.Text.Trim(), out int remotePort))
            {
                settings.RemoteControlPort = remotePort;
            }
        }

        if (_themeSettingsPageView != null)
        {
            settings.TitleColor = GetSelectedTitleColor();
            settings.ArtistColor = GetSelectedArtistColor();
            settings.LyricsColor = GetSelectedLyricsColor();
            settings.TitleOpacity = _themeSettingsPageView.TitleOpacitySlider.Value / 100.0;
            settings.ArtistOpacity = _themeSettingsPageView.ArtistOpacitySlider.Value / 100.0;
            settings.LyricsOpacity = _themeSettingsPageView.LyricsOpacitySlider.Value / 100.0;
        }

        _activeSettings = OverlaySettingsService.NormalizeForTests(settings);
        UpdatePlaybackSessionConfiguration();
        _smtcLyricTimingController.SetDelayOverrideMilliseconds(settings.SmtcLyricDelayOverrideMs);
        _overlayWindow.ApplySettings(_activeSettings);
        ApplyGamepadSettings(_activeSettings);
        ApplyAutoStart(_activeSettings.AutoStartOnBoot);
        ApplyDisplayColors(_activeSettings);
        UpdateOverlayControlLabels();
        UpdateRemoteControlPage();
    }

    private void UpdatePlaybackSessionConfiguration()
    {
        bool supportsTimeline = _playbackCoordinator
            .GetCapabilities(_activeSettings.TrackSource)
            .HasFlag(PlaybackSourceCapabilities.PlaybackState);
        bool allowSelectedTimeline = IsSmtcSource() ||
            _activeSettings.EnableNeteaseMemoryTimeline;
        bool timelineEnabled = supportsTimeline && allowSelectedTimeline &&
            (_activeSettings.EnableLyrics ||
             (_activeSettings.HideOverlayWhenPaused && IsSmtcSource()));
        _playbackSession.Configure(_activeSettings.TrackSource, timelineEnabled);
    }

    private void UpdateOverlayControlLabels()
    {
        if (_floatingSettingsPageView != null)
        {
            _floatingSettingsPageView.HorizontalValueText.Text = $"{_floatingSettingsPageView.HorizontalSlider.Value:0}%";
            _floatingSettingsPageView.BottomOffsetValueText.Text = $"{_floatingSettingsPageView.BottomOffsetSlider.Value:0}%";
            _floatingSettingsPageView.ScaleValueText.Text = $"{_floatingSettingsPageView.ScaleSlider.Value:0}%";
        }

        if (_themeSettingsPageView != null)
        {
            _themeSettingsPageView.TitleOpacityValueText.Text = $"{_themeSettingsPageView.TitleOpacitySlider.Value:0}%";
            _themeSettingsPageView.ArtistOpacityValueText.Text = $"{_themeSettingsPageView.ArtistOpacitySlider.Value:0}%";
            _themeSettingsPageView.LyricsOpacityValueText.Text = $"{_themeSettingsPageView.LyricsOpacitySlider.Value:0}%";
        }

        if (_remoteControlPageView != null)
        {
            _remoteControlPageView.RemoteControlPortBox.Text = _activeSettings.RemoteControlPort.ToString();
        }
    }

    private void SelectTitleColor(string color)
    {
        if (_themeSettingsPageView == null)
        {
            return;
        }

        color = color.ToUpperInvariant();
        _themeSettingsPageView.TitleColor_White.IsChecked = color == "#FFFFFF";
        _themeSettingsPageView.TitleColor_Light.IsChecked = color == "#D0E0F0";
        _themeSettingsPageView.TitleColor_Yellow.IsChecked = color == "#F0E080";
        _themeSettingsPageView.TitleColor_Green.IsChecked = color == "#90EE90";
        _themeSettingsPageView.TitleColor_Orange.IsChecked = color == "#FFB366";
    }

    private void SelectArtistColor(string color)
    {
        if (_themeSettingsPageView == null)
        {
            return;
        }

        color = color.ToUpperInvariant();
        _themeSettingsPageView.ArtistColor_Light.IsChecked = color == "#C0D0E0";
        _themeSettingsPageView.ArtistColor_White.IsChecked = color == "#FFFFFF";
        _themeSettingsPageView.ArtistColor_Yellow.IsChecked = color == "#F0E080";
        _themeSettingsPageView.ArtistColor_Green.IsChecked = color == "#90EE90";
        _themeSettingsPageView.ArtistColor_Orange.IsChecked = color == "#FFB366";
    }

    private string GetSelectedTitleColor()
    {
        if (TitleColor_White.IsChecked == true) return "#FFFFFF";
        if (TitleColor_Light.IsChecked == true) return "#D0E0F0";
        if (TitleColor_Yellow.IsChecked == true) return "#F0E080";
        if (TitleColor_Green.IsChecked == true) return "#90EE90";
        if (TitleColor_Orange.IsChecked == true) return "#FFB366";
        return "#FFFFFF";
    }

    private string GetSelectedArtistColor()
    {
        if (ArtistColor_Light.IsChecked == true) return "#C0D0E0";
        if (ArtistColor_White.IsChecked == true) return "#FFFFFF";
        if (ArtistColor_Yellow.IsChecked == true) return "#F0E080";
        if (ArtistColor_Green.IsChecked == true) return "#90EE90";
        if (ArtistColor_Orange.IsChecked == true) return "#FFB366";
        return "#C0D0E0";
    }

    private void SelectLyricsColor(string color)
    {
        if (_themeSettingsPageView == null)
        {
            return;
        }

        color = color.ToUpperInvariant();
        _themeSettingsPageView.LyricsColor_Light.IsChecked = color == "#A0B8D0";
        _themeSettingsPageView.LyricsColor_White.IsChecked = color == "#FFFFFF";
        _themeSettingsPageView.LyricsColor_Yellow.IsChecked = color == "#F0E080";
        _themeSettingsPageView.LyricsColor_Green.IsChecked = color == "#90EE90";
        _themeSettingsPageView.LyricsColor_Orange.IsChecked = color == "#FFB366";
    }

    private string GetSelectedLyricsColor()
    {
        if (LyricsColor_Light.IsChecked == true) return "#A0B8D0";
        if (LyricsColor_White.IsChecked == true) return "#FFFFFF";
        if (LyricsColor_Yellow.IsChecked == true) return "#F0E080";
        if (LyricsColor_Green.IsChecked == true) return "#90EE90";
        if (LyricsColor_Orange.IsChecked == true) return "#FFB366";
        return "#A0B8D0";
    }

    private void ApplyDisplayColors(OverlaySettings settings)
    {
        if (_nowPlayingPageView != null)
        {
            _nowPlayingPageView.CurrentTitle.ClearValue(TextBlock.ForegroundProperty);
            _nowPlayingPageView.CurrentTitle.ClearValue(UIElement.OpacityProperty);
            _nowPlayingPageView.CurrentArtist.ClearValue(TextBlock.ForegroundProperty);
            _nowPlayingPageView.CurrentArtist.ClearValue(UIElement.OpacityProperty);
        }

        if (_themeSettingsPageView == null)
        {
            return;
        }

        try
        {
            var titleColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.TitleColor);
            var titleBrush = new System.Windows.Media.SolidColorBrush(titleColor);
            _themeSettingsPageView.ThemePreviewTitle.Foreground = titleBrush.Clone();
            _themeSettingsPageView.ThemePreviewTitle.Opacity = settings.TitleOpacity;
        }
        catch { }

        try
        {
            var artistColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.ArtistColor);
            var artistBrush = new System.Windows.Media.SolidColorBrush(artistColor);
            _themeSettingsPageView.ThemePreviewArtist.Foreground = artistBrush.Clone();
            _themeSettingsPageView.ThemePreviewArtist.Opacity = settings.ArtistOpacity;
        }
        catch { }

        try
        {
            var lyricsColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.LyricsColor);
            _themeSettingsPageView.ThemePreviewLyrics.Foreground = new System.Windows.Media.SolidColorBrush(lyricsColor);
            _themeSettingsPageView.ThemePreviewLyrics.Opacity = settings.LyricsOpacity;
        }
        catch { }
    }

    private void TitleColor_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
    }

    private void ArtistColor_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
    }

    private void TitleOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
    }

    private void ArtistOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
    }

    private void LyricsColor_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
    }

    private void LyricsOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingOverlayControls) return;
        ApplyOverlaySettingsFromControls();
    }

    private bool RebindGlobalHotkeys()
    {
        _hotkeyService?.Dispose();

        _hotkeyService = new GlobalHotkeyService(this);
        _hotkeyService.NextRequested += async (_, _) => await NextAsync();
        _hotkeyService.PrevRequested += async (_, _) => await PrevAsync();
        _hotkeyService.TogglePlayPauseRequested += async (_, _) => await TogglePlayPauseAsync();
        _hotkeyService.ToggleOverlayRequested += async (_, _) => await ToggleOverlayVisibilityAsync();

        return _hotkeyService.Register(
            _activeSettings.AppPrevHotkey,
            _activeSettings.AppNextHotkey,
            _activeSettings.AppToggleHotkey,
            _activeSettings.AppToggleOverlayHotkey);
    }

    private static bool AreHotkeysValid(OverlaySettings settings)
    {
        bool keyboardOk = HotkeyParser.TryParse(settings.AppPrevHotkey, out _) &&
                          HotkeyParser.TryParse(settings.AppNextHotkey, out _) &&
                          HotkeyParser.TryParse(settings.AppToggleHotkey, out _) &&
                          HotkeyParser.TryParse(settings.AppToggleOverlayHotkey, out _) &&
                          HotkeyParser.TryParse(settings.NeteasePrevHotkey, out _) &&
                          HotkeyParser.TryParse(settings.NeteaseNextHotkey, out _) &&
                          HotkeyParser.TryParse(settings.NeteaseToggleHotkey, out _);

        if (!keyboardOk)
        {
            return false;
        }

        if (!settings.EnableGamepadHotkeys)
        {
            return true;
        }

        return GamepadHotkeyParser.TryParse(settings.GamepadPrevHotkey, out _) &&
               GamepadHotkeyParser.TryParse(settings.GamepadNextHotkey, out _) &&
               GamepadHotkeyParser.TryParse(settings.GamepadToggleHotkey, out _) &&
               GamepadHotkeyParser.TryParse(settings.GamepadToggleOverlayHotkey, out _);
    }

    private void ApplyGamepadSettings(OverlaySettings settings)
    {
        _gamepadInputService.Enabled = settings.EnableGamepadHotkeys;

        if (GamepadHotkeyParser.TryParse(settings.GamepadPrevHotkey, out var prev))
        {
            _gamepadInputService.PrevHotkey = prev;
        }

        if (GamepadHotkeyParser.TryParse(settings.GamepadNextHotkey, out var next))
        {
            _gamepadInputService.NextHotkey = next;
        }

        if (GamepadHotkeyParser.TryParse(settings.GamepadToggleHotkey, out var toggle))
        {
            _gamepadInputService.ToggleHotkey = toggle;
        }

        if (GamepadHotkeyParser.TryParse(settings.GamepadToggleOverlayHotkey, out var toggleOverlay))
        {
            _gamepadInputService.ToggleOverlayHotkey = toggleOverlay;
        }
    }

    private static void ApplyAutoStart(bool enable)
    {
        if (!RuntimeFeatureSupport.SupportsRegistryAutoStart())
        {
            return;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null)
            {
                return;
            }

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue("HorizonRadioOverlay", AppLaunchPolicy.BuildAutoStartCommand(exePath));
                }
            }
            else
            {
                key.DeleteValue("HorizonRadioOverlay", false);
            }
        }
        catch { }
    }
}
