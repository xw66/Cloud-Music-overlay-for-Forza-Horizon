using System.Windows.Media;

namespace HorizonRadioOverlay.Models;

public sealed class CoverFlowAlbum
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public ImageSource? CoverImage { get; init; }
}
