namespace HorizonRadioOverlay.Models;

public sealed class UpdateInfo
{
    public bool HasUpdate { get; init; }
    public string LatestVersion { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}
