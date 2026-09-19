using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HorizonRadioOverlay.ViewModels;

[SupportedOSPlatform("windows")]
public sealed partial class AboutViewModel : ObservableObject
{
    public const string ProjectUrl = "https://github.com/xw66/Cloud-Music-overlay-for-Forza-Horizon";
    public const string LicenseUrl = "https://github.com/xw66/Cloud-Music-overlay-for-Forza-Horizon/blob/main/LICENSE";

    [ObservableProperty]
    private string _appTitle = UiText.AppTitle;

    [ObservableProperty]
    private string _versionText = $"{UiText.VersionPrefix} {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "3.0.2"}";

    [ObservableProperty]
    private string _description = UiText.AboutDescription;

    public event Action? RequestCheckUpdate;
    public event Action<string, bool>? StatusNotification;

    [RelayCommand]
    private void OpenProjectHome()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ProjectUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            StatusNotification?.Invoke("状态：无法打开浏览器。", true);
        }
    }

    [RelayCommand]
    private void CheckUpdate()
    {
        RequestCheckUpdate?.Invoke();
    }

    [RelayCommand]
    private void OpenLicense()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = LicenseUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            StatusNotification?.Invoke("状态：无法打开浏览器。", true);
        }
    }
}
