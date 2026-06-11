using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.IO;

namespace HorizonRadioOverlay.Services;

public static class DiagnosticContext
{
    public static string NewTraceId() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    public static string Format(
        string traceId,
        string component,
        string stage,
        params (string Key, object? Value)[] fields)
    {
        string values = string.Join(" ", fields.Select(x => $"{x.Key}={FormatValue(x.Value)}"));
        return $"[trace={traceId}] [component={component}] [stage={stage}]" +
            (values.Length > 0 ? $" {values}" : string.Empty);
    }

    public static string ClassifyException(Exception exception)
    {
        Exception root = exception is AggregateException aggregate
            ? aggregate.GetBaseException()
            : exception;

        return root switch
        {
            TaskCanceledException => "timeout",
            TimeoutException => "timeout",
            UnauthorizedAccessException => "file-access-denied",
            AuthenticationException => "proxy-or-tls-failed",
            SocketException socket when socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData
                => "dns-failed",
            HttpRequestException http when http.InnerException is AuthenticationException
                => "proxy-or-tls-failed",
            HttpRequestException http when http.InnerException is SocketException socket &&
                                           socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData
                => "dns-failed",
            HttpRequestException => "network-request-failed",
            System.Text.Json.JsonException => "invalid-json",
            IOException => "file-io-failed",
            _ => "unknown-error"
        };
    }

    public static string ClassifyHttpStatus(HttpStatusCode statusCode) => $"http-{(int)statusCode}";

    public static bool IsLikelyImage(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 4)
        {
            return false;
        }

        return (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) ||
               (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) ||
               (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38) ||
               (bytes.Length >= 12 &&
                bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50);
    }

    public static string GetSuggestion(string rootCause)
    {
        if (rootCause == "dns-failed") return "检查 DNS、代理或是否能访问 music.163.com";
        if (rootCause == "timeout") return "检查网络延迟、防火墙或代理";
        if (rootCause == "proxy-or-tls-failed") return "检查系统代理、证书和 HTTPS 拦截软件";
        if (rootCause == "file-access-denied") return "检查网易云数据目录和缓存目录权限";
        if (rootCause == "invalid-image") return "封面地址返回的不是有效图片，检查响应类型和网络拦截";
        if (rootCause.StartsWith("http-4", StringComparison.Ordinal)) return "请求被拒绝或资源不存在，检查网络区域和接口可用性";
        if (rootCause.StartsWith("http-5", StringComparison.Ordinal)) return "网易云服务暂时异常，稍后重试";
        if (rootCause == "resolve-low-confidence") return "窗口标题与官方搜索结果不够匹配";
        return "查看同一 trace 的上一条失败阶段和异常详情";
    }

    private static string FormatValue(object? value)
    {
        string text = value?.ToString() ?? "<none>";
        text = text.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
        return text.Any(char.IsWhiteSpace) ? $"\"{text}\"" : text;
    }
}
