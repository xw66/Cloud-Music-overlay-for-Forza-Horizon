using System.Windows.Media;
using HorizonRadioOverlay.Services;
using HorizonRadioOverlay.ViewModels;

namespace HorizonRadioOverlay.Tests;

public sealed class ColorPickerTests
{
    [Fact]
    public void ColorToHsv_and_HsvToColor_roundtrips_pure_colors()
    {
        // 红色 #FF0000
        Color red = Color.FromRgb(255, 0, 0);
        var (h, s, v) = ColorUtils.ColorToHsv(red);
        Assert.Equal(0.0, h, 1);
        Assert.Equal(1.0, s, 2);
        Assert.Equal(1.0, v, 2);

        Color roundtripRed = ColorUtils.HsvToColor(h, s, v);
        Assert.Equal(red.R, roundtripRed.R);
        Assert.Equal(red.G, roundtripRed.G);
        Assert.Equal(red.B, roundtripRed.B);
    }

    [Theory]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#3B82F6", 59, 130, 246)]
    [InlineData("FFB366", 255, 179, 102)]
    public void TryParseHex_parses_valid_hex_strings(string hex, byte r, byte g, byte b)
    {
        bool success = ColorUtils.TryParseHex(hex, out Color color);
        Assert.True(success);
        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
    }

    [Fact]
    public void ThemeSettingsViewModel_ApplyColorFromPicker_updates_active_target()
    {
        ThemeSettingsViewModel vm = new();

        // 默认目标为 Title
        vm.SetActiveTarget("Title");
        vm.ApplyColorFromPicker("#123456");
        Assert.Equal("#123456", vm.TitleColor);
        Assert.Equal("#123456", vm.ActiveColorHex);

        // 切换目标为 Artist
        vm.SetActiveTarget("Artist");
        vm.ApplyColorFromPicker("#654321");
        Assert.Equal("#654321", vm.ArtistColor);
        Assert.Equal("#654321", vm.ActiveColorHex);

        // 切换目标为 Lyrics
        vm.SetActiveTarget("Lyrics");
        vm.ApplyColorFromPicker("#ABCDEF");
        Assert.Equal("#ABCDEF", vm.LyricsColor);
        Assert.Equal("#ABCDEF", vm.ActiveColorHex);
    }
}
