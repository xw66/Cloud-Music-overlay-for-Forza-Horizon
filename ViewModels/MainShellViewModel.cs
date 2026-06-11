using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.ViewModels;

public sealed class MainShellViewModel : INotifyPropertyChanged
{
    private NavigationItem? _selectedItem;

    public MainShellViewModel()
    {
        NavigationItems =
        [
            new NavigationItem { Key = "NowPlaying", Title = UiText.NavNowPlaying, IconGlyph = "\uE189", Description = UiText.NavNowPlayingDesc },
            new NavigationItem { Key = "FloatingSettings", Title = UiText.NavFloatingSettings, IconGlyph = "\uE718", Description = UiText.NavFloatingSettingsDesc },
            new NavigationItem { Key = "Hotkeys", Title = UiText.NavHotkeys, IconGlyph = "\uE765", Description = UiText.NavHotkeysDesc },
            new NavigationItem { Key = "Theme", Title = UiText.NavTheme, IconGlyph = "\uE790", Description = UiText.NavThemeDesc },
            new NavigationItem { Key = "Logs", Title = UiText.NavLogs, IconGlyph = "\uE7BA", Description = UiText.NavLogsDesc },
            new NavigationItem { Key = "About", Title = UiText.NavAbout, IconGlyph = "\uE946", Description = UiText.NavAboutDesc }
        ];

        _selectedItem = NavigationItems[0];
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public NavigationItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value))
            {
                return;
            }

            _selectedItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPageTitle));
            OnPropertyChanged(nameof(CurrentPageDescription));
        }
    }

    public string CurrentPageTitle => SelectedItem?.Title ?? UiText.AppTitle;

    public string CurrentPageDescription => SelectedItem?.Description ?? string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
