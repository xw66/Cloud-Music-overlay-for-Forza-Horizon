using System.Net.Http;

namespace HorizonRadioOverlay.Services;

internal static class NeteaseHttpPolicy
{
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    public const string DesktopClientCookie =
        "os=pc; osver=Microsoft-Windows-10-Professional-build-19045-64bit; appver=3.0.0; channel=netease; __remember_me=true;";

    public const string DomesticProxyIp = "116.25.146.177";

    public static void ApplyHeaders(HttpRequestMessage request)
    {
        request.Headers.Referrer = new Uri("https://music.163.com/");
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.ParseAdd(DefaultUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        request.Headers.TryAddWithoutValidation("Cookie", DesktopClientCookie);
        request.Headers.TryAddWithoutValidation("X-Real-IP", DomesticProxyIp);
    }

    public static HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request);
        return request;
    }
}
