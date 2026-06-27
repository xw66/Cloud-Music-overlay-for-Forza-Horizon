using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using QRCoder;

namespace HorizonRadioOverlay.Views.Pages;

public partial class RemoteControlPage : UserControl
{
    public RemoteControlPage()
    {
        InitializeComponent();
    }

    public void SetQrCode(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            RemoteControlQrImage.Source = null;
            return;
        }

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
        RemoteControlQrImage.Source = image;
    }
}
