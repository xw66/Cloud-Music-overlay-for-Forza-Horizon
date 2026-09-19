using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.Services;
using QRCoder;

namespace HorizonRadioOverlay.ViewModels;

[SupportedOSPlatform("windows")]
public sealed partial class RemoteControlViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _enableRemoteControl;

    [ObservableProperty]
    private string _portText = RemoteControlPolicy.DefaultPort.ToString();

    [ObservableProperty]
    private bool _allowLan = true;

    [ObservableProperty]
    private string _statusText = "未启用";

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _helpText = string.Empty;

    [ObservableProperty]
    private ImageSource? _qrCodeImage;

    public event Action? SettingsChanged;
    public event Action? RequestResetToken;
    public event Action<string, bool>? StatusNotification;

    partial void OnEnableRemoteControlChanged(bool value) => SettingsChanged?.Invoke();
    partial void OnPortTextChanged(string value) => SettingsChanged?.Invoke();
    partial void OnAllowLanChanged(bool value) => SettingsChanged?.Invoke();

    [RelayCommand]
    private void CopyAddress()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Address))
            {
                StatusNotification?.Invoke("状态：手机遥控地址为空，请先启用遥控服务。", true);
                return;
            }

            Clipboard.SetText(Address);
            StatusNotification?.Invoke("状态：手机遥控地址已复制。", false);
        }
        catch (Exception ex)
        {
            StatusNotification?.Invoke($"状态：复制手机遥控地址失败。{ex.Message}", true);
        }
    }

    [RelayCommand]
    private void ResetToken()
    {
        RequestResetToken?.Invoke();
    }

    public void LoadFromSettings(OverlaySettings settings)
    {
        EnableRemoteControl = settings.EnableRemoteControl;
        PortText = settings.RemoteControlPort.ToString();
        AllowLan = settings.RemoteControlAllowLan;
    }

    public void ApplyToSettings(OverlaySettings settings)
    {
        settings.EnableRemoteControl = EnableRemoteControl;
        if (int.TryParse(PortText, out int port) && port is >= 1024 and <= 65535)
        {
            settings.RemoteControlPort = port;
        }
        settings.RemoteControlAllowLan = AllowLan;
    }

    public void UpdateRuntimeStatus(RemoteControlService service, OverlaySettings settings)
    {
        string displayUrl = settings.RemoteControlAllowLan ? service.LanUrl : service.LocalUrl;
        StatusText = settings.EnableRemoteControl ? service.StatusMessage : "未启用";
        Address = settings.EnableRemoteControl ? displayUrl : string.Empty;
        HelpText = BuildHelpText(service, settings);
        QrCodeImage = GenerateQrCode(Address);
    }

    private static string BuildHelpText(RemoteControlService service, OverlaySettings settings)
    {
        if (!settings.EnableRemoteControl)
        {
            return "启用后会生成手机访问地址和二维码。";
        }

        if (!service.IsRunning)
        {
            return "服务未运行。请检查端口是否被占用，或换一个端口后重新启用。";
        }

        if (!settings.RemoteControlAllowLan)
        {
            return "当前只允许本机访问。若要用手机连接，请勾选允许局域网手机访问。";
        }

        IReadOnlyList<string> urls = service.LanUrls;
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

    private static ImageSource? GenerateQrCode(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            using QRCodeGenerator generator = new();
            using QRCodeData data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            PngByteQRCode qrCode = new(data);
            byte[] bytes = qrCode.GetGraphic(12);
            BitmapImage image = new();
            using MemoryStream stream = new(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
