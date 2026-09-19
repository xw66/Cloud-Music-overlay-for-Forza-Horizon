using System.Runtime.Versioning;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.ViewModels;

[SupportedOSPlatform("windows")]
public sealed partial class ThemeSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _titleColor = "#FFFFFF";

    [ObservableProperty]
    private string _artistColor = "#C0D0E0";

    [ObservableProperty]
    private string _lyricsColor = "#A0B8D0";

    [ObservableProperty]
    private double _titleOpacity = 100.0;

    [ObservableProperty]
    private double _artistOpacity = 86.0;

    [ObservableProperty]
    private double _lyricsOpacity = 70.0;

    [ObservableProperty]
    private int _previewEffectLevel = 1;

    [ObservableProperty]
    private Brush _titleBrush = Brushes.White;

    [ObservableProperty]
    private Brush _artistBrush = new SolidColorBrush(Color.FromRgb(192, 208, 224));

    [ObservableProperty]
    private Brush _lyricsBrush = new SolidColorBrush(Color.FromRgb(160, 184, 208));

    public string TitleOpacityText => $"{TitleOpacity:0}%";
    public string ArtistOpacityText => $"{ArtistOpacity:0}%";
    public string LyricsOpacityText => $"{LyricsOpacity:0}%";

    public double TitleOpacityRatio => Math.Clamp(TitleOpacity / 100.0, 0.2, 1.0);
    public double ArtistOpacityRatio => Math.Clamp(ArtistOpacity / 100.0, 0.2, 1.0);
    public double LyricsOpacityRatio => Math.Clamp(LyricsOpacity / 100.0, 0.2, 1.0);

    public event Action? SettingsChanged;

    private bool _suppressNotification;

    partial void OnTitleColorChanged(string value)
    {
        UpdateBrushes();
        NotifySettingsChanged();
    }

    partial void OnArtistColorChanged(string value)
    {
        UpdateBrushes();
        NotifySettingsChanged();
    }

    partial void OnLyricsColorChanged(string value)
    {
        UpdateBrushes();
        NotifySettingsChanged();
    }

    partial void OnTitleOpacityChanged(double value)
    {
        OnPropertyChanged(nameof(TitleOpacityText));
        OnPropertyChanged(nameof(TitleOpacityRatio));
        NotifySettingsChanged();
    }

    partial void OnArtistOpacityChanged(double value)
    {
        OnPropertyChanged(nameof(ArtistOpacityText));
        OnPropertyChanged(nameof(ArtistOpacityRatio));
        NotifySettingsChanged();
    }

    partial void OnLyricsOpacityChanged(double value)
    {
        OnPropertyChanged(nameof(LyricsOpacityText));
        OnPropertyChanged(nameof(LyricsOpacityRatio));
        NotifySettingsChanged();
    }

    partial void OnPreviewEffectLevelChanged(int value)
    {
        NotifySettingsChanged();
    }

    [RelayCommand]
    public void SelectTitleColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            TitleColor = hex;
        }
    }

    [RelayCommand]
    public void SelectArtistColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            ArtistColor = hex;
        }
    }

    [RelayCommand]
    public void SelectLyricsColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            LyricsColor = hex;
        }
    }

    [RelayCommand]
    public void SetPreviewEffect(string? levelStr)
    {
        if (int.TryParse(levelStr, out int level))
        {
            PreviewEffectLevel = Math.Clamp(level, 0, 2);
        }
    }

    private void UpdateBrushes()
    {
        try
        {
            if (ColorConverter.ConvertFromString(TitleColor) is Color c1)
            {
                TitleBrush = new SolidColorBrush(c1);
            }
        }
        catch { }

        try
        {
            if (ColorConverter.ConvertFromString(ArtistColor) is Color c2)
            {
                ArtistBrush = new SolidColorBrush(c2);
            }
        }
        catch { }

        try
        {
            if (ColorConverter.ConvertFromString(LyricsColor) is Color c3)
            {
                LyricsBrush = new SolidColorBrush(c3);
            }
        }
        catch { }
    }

    private void NotifySettingsChanged()
    {
        if (_suppressNotification) return;
        SettingsChanged?.Invoke();
    }

    public void LoadFrom(OverlaySettings settings)
    {
        _suppressNotification = true;
        try
        {
            TitleColor = settings.TitleColor;
            ArtistColor = settings.ArtistColor;
            LyricsColor = settings.LyricsColor;
            TitleOpacity = settings.TitleOpacity * 100.0;
            ArtistOpacity = settings.ArtistOpacity * 100.0;
            LyricsOpacity = settings.LyricsOpacity * 100.0;
            UpdateBrushes();
        }
        finally
        {
            _suppressNotification = false;
        }
    }

    public void ApplyTo(OverlaySettings settings)
    {
        settings.TitleColor = TitleColor;
        settings.ArtistColor = ArtistColor;
        settings.LyricsColor = LyricsColor;
        settings.TitleOpacity = TitleOpacityRatio;
        settings.ArtistOpacity = ArtistOpacityRatio;
        settings.LyricsOpacity = LyricsOpacityRatio;
    }
}
