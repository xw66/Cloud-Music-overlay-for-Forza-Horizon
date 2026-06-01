using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class UpdateService : IDisposable
{
    private const string GitHubApiUrl = "https://api.github.com/repos/xw66/Cloud-Music-overlay-for-Forza-Horizon/releases/latest";
    private static readonly Version CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HorizonRadioOverlay/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<UpdateInfo> CheckForUpdateAsync()
    {
        try
        {
            string json = await _httpClient.GetStringAsync(GitHubApiUrl);

            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "" : "";
            string releaseNotes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";

            string downloadUrl = "";
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.Contains("win-x64"))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() ?? "" : "";
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                downloadUrl = root.TryGetProperty("html_url", out var html) ? html.GetString() ?? "" : "";
            }

            Version? latestVersion = ParseVersion(tagName);
            if (latestVersion == null)
            {
                return new UpdateInfo { ErrorMessage = "无法解析最新版本号" };
            }

            bool hasUpdate = latestVersion > CurrentVersion;

            return new UpdateInfo
            {
                HasUpdate = hasUpdate,
                LatestVersion = tagName,
                DownloadUrl = downloadUrl,
                ReleaseNotes = releaseNotes
            };
        }
        catch (TaskCanceledException)
        {
            return new UpdateInfo { ErrorMessage = "检查超时，请检查网络连接" };
        }
        catch (HttpRequestException ex)
        {
            return new UpdateInfo { ErrorMessage = $"网络请求失败：{ex.Message}" };
        }
        catch (Exception ex)
        {
            return new UpdateInfo { ErrorMessage = $"检查失败：{ex.Message}" };
        }
    }

    private static Version? ParseVersion(string tag)
    {
        string cleaned = tag.TrimStart('v', 'V', ' ');
        if (Version.TryParse(cleaned, out var version))
        {
            return version;
        }

        string[] parts = cleaned.Split('.');
        if (parts.Length >= 2 && int.TryParse(parts[0], out int major) && int.TryParse(parts[1], out int minor))
        {
            int build = parts.Length >= 3 && int.TryParse(parts[2], out int b) ? b : 0;
            int rev = parts.Length >= 4 && int.TryParse(parts[3], out int r) ? r : 0;
            return new Version(major, minor, build, rev);
        }

        return null;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
