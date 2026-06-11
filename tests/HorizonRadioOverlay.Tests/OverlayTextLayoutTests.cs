namespace HorizonRadioOverlay.Tests;

public sealed class OverlayTextLayoutTests
{
    [Fact]
    public void Overlay_track_text_is_single_line_centered_and_not_trimmed()
    {
        string xaml = File.ReadAllText(FindWorkspaceFile("OverlayWindow.xaml"));

        Assert.Contains("Name=\"TitleText\"", xaml);
        Assert.Contains("Name=\"ArtistText\"", xaml);
        Assert.Contains("Name=\"LyricsText\"", xaml);
        Assert.DoesNotContain("TextWrapping=\"Wrap\"", xaml);
        Assert.DoesNotContain("TextTrimming=\"CharacterEllipsis\"", xaml);
        Assert.DoesNotContain("MaxWidth=\"188\"", xaml);
    }

    [Theory]
    [InlineData("Views/Pages/NowPlayingPage.xaml")]
    [InlineData("Views/Pages/ThemeSettingsPage.xaml")]
    public void Main_window_track_preview_text_is_single_line_and_not_trimmed(string relativePath)
    {
        string xaml = File.ReadAllText(FindWorkspaceFile(relativePath));

        foreach (string name in new[] { "CurrentTitle", "CurrentArtist", "LyricsPreviewText", "ThemePreviewTitle", "ThemePreviewArtist", "ThemePreviewLyrics" })
        {
            if (!xaml.Contains($"x:Name=\"{name}\"", StringComparison.Ordinal))
            {
                continue;
            }

            int start = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
            int end = xaml.IndexOf("/>", start, StringComparison.Ordinal);
            string element = xaml[start..end];
            Assert.Contains("TextWrapping=\"NoWrap\"", element);
            Assert.Contains("TextTrimming=\"None\"", element);
            Assert.Contains("HorizontalAlignment=\"Center\"", element);
        }
    }

    private static string FindWorkspaceFile(string fileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                fileName.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(fileName);
    }
}
