using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HorizonRadioOverlay.Models;

namespace HorizonRadioOverlay.Services;

public sealed class RemoteControlService : IDisposable
{
    private Func<Task<RemoteControlStatus>> _statusProvider;
    private Func<string, Task> _controlHandler;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private OverlaySettings? _settings;

    public event EventHandler? StateChanged;

    public bool IsRunning { get; private set; }

    public string StatusMessage { get; private set; } = "未启用";

    public string? LastError { get; private set; }

    public string LocalUrl => BuildLocalUrl(_settings);

    public string LanUrl => BuildLanUrl(_settings);

    public IReadOnlyList<string> LanUrls => BuildLanUrls(_settings);

    public RemoteControlService()
        : this(() => Task.FromResult(new RemoteControlStatus()), _ => Task.CompletedTask)
    {
    }

    public RemoteControlService(
        Func<Task<RemoteControlStatus>> statusProvider,
        Func<string, Task> controlHandler)
    {
        _statusProvider = statusProvider;
        _controlHandler = controlHandler;
    }

    public void SetHandlers(
        Func<Task<RemoteControlStatus>> statusProvider,
        Func<string, Task> controlHandler)
    {
        _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
        _controlHandler = controlHandler ?? throw new ArgumentNullException(nameof(controlHandler));
    }

    public static string GenerateToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    public void ApplySettings(OverlaySettings settings)
    {
        bool shouldRestart = IsRunning &&
            (_settings == null ||
             _settings.RemoteControlPort != settings.RemoteControlPort ||
             _settings.RemoteControlAllowLan != settings.RemoteControlAllowLan ||
             !string.Equals(_settings.RemoteControlToken, settings.RemoteControlToken, StringComparison.Ordinal));

        _settings = CloneRemoteSettings(settings);

        if (!settings.EnableRemoteControl)
        {
            Stop();
            StatusMessage = "未启用";
            LastError = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (shouldRestart)
        {
            Stop();
        }

        if (!IsRunning)
        {
            Start();
        }
    }

    public void Stop()
    {
        if (!IsRunning && _listener == null && _cts == null)
        {
            return;
        }

        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        StatusMessage = "已停止";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Stop();
    }

    private void Start()
    {
        if (_settings == null)
        {
            return;
        }

        try
        {
            IPAddress address = _settings.RemoteControlAllowLan ? IPAddress.Any : IPAddress.Loopback;
            _listener = new TcpListener(address, _settings.RemoteControlPort);
            _listener.ExclusiveAddressUse = false;
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();
            _cts = new CancellationTokenSource();
            IsRunning = true;
            LastError = null;
            IReadOnlyList<string> lanUrls = LanUrls;
            StatusMessage = _settings.RemoteControlAllowLan && lanUrls.Count == 0
                ? $"运行中：未检测到可用局域网 IPv4，当前只能本机访问 {LocalUrl}"
                : _settings.RemoteControlAllowLan
                    ? $"运行中：{LanUrl}"
                    : $"运行中：{LocalUrl}";
            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }
        catch (SocketException ex)
        {
            IsRunning = false;
            LastError = ex.Message;
            StatusMessage = $"启动失败：端口 {_settings.RemoteControlPort} 可能被占用，或防火墙阻止访问。";
        }
        catch (Exception ex)
        {
            IsRunning = false;
            LastError = ex.Message;
            StatusMessage = $"启动失败：{ex.Message}";
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    StatusMessage = "运行异常：无法接受新的手机连接。";
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            using NetworkStream stream = client.GetStream();
            RemoteHttpRequest? request = await ReadRequestAsync(stream, cancellationToken);
            if (request == null)
            {
                return;
            }

            await RouteAsync(stream, request, cancellationToken);
        }
    }

    private async Task RouteAsync(NetworkStream stream, RemoteHttpRequest request, CancellationToken cancellationToken)
    {
        string path = request.Path;
        if (request.Method == "GET" && path == "/")
        {
            await WriteStringAsync(stream, 200, "text/html; charset=utf-8", BuildPageHtml(), cancellationToken);
            return;
        }

        if (request.Method == "GET" && path == "/manifest.webmanifest")
        {
            string manifest = JsonSerializer.Serialize(new
            {
                name = "网易云悬浮窗遥控",
                short_name = "遥控",
                start_url = "/",
                display = "standalone",
                background_color = "#101722",
                theme_color = "#5B5CEB"
            });
            await WriteStringAsync(stream, 200, "application/manifest+json; charset=utf-8", manifest, cancellationToken);
            return;
        }

        if (request.Method == "GET" && path == "/api/status")
        {
            if (!IsRequestAuthorized(request))
            {
                await WriteJsonAsync(stream, 401, new { error = "unauthorized" }, cancellationToken);
                return;
            }

            RemoteControlStatus status = await _statusProvider();
            await WriteJsonAsync(stream, 200, status, cancellationToken);
            return;
        }

        if (request.Method == "POST" && path == "/api/control")
        {
            if (!IsRequestAuthorized(request))
            {
                await WriteJsonAsync(stream, 401, new { error = "unauthorized" }, cancellationToken);
                return;
            }

            string? action = TryReadAction(request.Body);
            if (!RemoteControlPolicy.IsAllowedAction(action))
            {
                await WriteJsonAsync(stream, 400, new { error = "invalid_action" }, cancellationToken);
                return;
            }

            await _controlHandler(action!);
            await WriteJsonAsync(stream, 200, new { ok = true }, cancellationToken);
            return;
        }

        await WriteJsonAsync(stream, 404, new { error = "not_found" }, cancellationToken);
    }

    private bool IsRequestAuthorized(RemoteHttpRequest request)
    {
        if (_settings == null)
        {
            return false;
        }

        string? token = request.Query.TryGetValue("token", out string? queryToken)
            ? queryToken
            : request.Headers.TryGetValue("x-remote-token", out string? headerToken)
                ? headerToken
                : null;
        return RemoteControlPolicy.IsAuthorized(token, _settings.RemoteControlToken);
    }

    private static string? TryReadAction(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("action", out JsonElement action))
            {
                return action.GetString();
            }
        }
        catch
        {
        }

        return null;
    }

    private async Task<RemoteHttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        List<byte> bytes = new();
        byte[] buffer = new byte[2048];
        int headerEnd = -1;

        while (bytes.Count < 65536 && headerEnd < 0)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                return null;
            }

            for (int i = 0; i < read; i++)
            {
                bytes.Add(buffer[i]);
            }

            headerEnd = FindHeaderEnd(bytes);
        }

        if (headerEnd < 0)
        {
            return null;
        }

        string headerText = Encoding.UTF8.GetString(bytes.Take(headerEnd).ToArray());
        string[] lines = headerText.Split("\r\n", StringSplitOptions.None);
        string[] requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2)
        {
            return null;
        }

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0) continue;
            headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        int contentLength = headers.TryGetValue("Content-Length", out string? lengthText) &&
                            int.TryParse(lengthText, out int parsedLength)
            ? parsedLength
            : 0;

        int bodyStart = headerEnd + 4;
        while (bytes.Count - bodyStart < contentLength && bytes.Count < 131072)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0) break;
            for (int i = 0; i < read; i++)
            {
                bytes.Add(buffer[i]);
            }
        }

        string target = requestLine[1];
        string path = target;
        Dictionary<string, string> query = new(StringComparer.OrdinalIgnoreCase);
        int queryIndex = target.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = target[..queryIndex];
            query = ParseQuery(target[(queryIndex + 1)..]);
        }

        string body = contentLength > 0 && bytes.Count >= bodyStart
            ? Encoding.UTF8.GetString(bytes.Skip(bodyStart).Take(contentLength).ToArray())
            : string.Empty;

        return new RemoteHttpRequest(requestLine[0].ToUpperInvariant(), path, query, headers, body);
    }

    private static int FindHeaderEnd(List<byte> bytes)
    {
        for (int i = 3; i < bytes.Count; i++)
        {
            if (bytes[i - 3] == '\r' && bytes[i - 2] == '\n' && bytes[i - 1] == '\r' && bytes[i] == '\n')
            {
                return i - 3;
            }
        }

        return -1;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            string key = WebUtility.UrlDecode(pair[0]);
            string value = pair.Length > 1 ? WebUtility.UrlDecode(pair[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static async Task WriteJsonAsync(NetworkStream stream, int status, object value, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await WriteStringAsync(stream, status, "application/json; charset=utf-8", json, cancellationToken);
    }

    private static async Task WriteStringAsync(NetworkStream stream, int status, string contentType, string body, CancellationToken cancellationToken)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            _ => "Error"
        };
        string header =
            $"HTTP/1.1 {status} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
    }

    private string BuildPageHtml()
    {
        string token = WebUtility.HtmlEncode(_settings?.RemoteControlToken ?? string.Empty);
        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<meta name="theme-color" content="#5B5CEB">
<link rel="manifest" href="/manifest.webmanifest">
<title>网易云悬浮窗遥控</title>
<style>
:root{color-scheme:dark;font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;background:#101722;color:#eef3ff}
*{box-sizing:border-box}body{margin:0;min-height:100vh;background:linear-gradient(180deg,#121b2a,#0b1018);padding:24px}
.wrap{max-width:520px;margin:0 auto}.top{padding:18px 0 22px}.k{font-size:13px;color:#91a0bb}.title{font-size:30px;font-weight:750;margin-top:6px;line-height:1.12}.artist{font-size:17px;color:#c5d0e5;margin-top:8px}.meta{margin-top:12px;color:#8ea0be;font-size:13px}
.grid{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:24px}.wide{grid-column:1 / span 2}
button{border:0;border-radius:18px;padding:22px 16px;min-height:88px;background:#25314a;color:#fff;font-size:20px;font-weight:700;box-shadow:0 10px 24px rgba(0,0,0,.22)}
button:active{transform:translateY(1px);background:#5b5ceb}.primary{background:#5b5ceb}.status{margin-top:18px;color:#91a0bb;font-size:13px;line-height:1.6}
</style>
</head>
<body>
<main class="wrap">
<section class="top">
<div class="k">网易云悬浮窗遥控</div>
<div id="title" class="title">正在连接</div>
<div id="artist" class="artist">请保持手机和电脑在同一 Wi-Fi</div>
<div id="meta" class="meta">-</div>
</section>
<section class="grid">
<button onclick="send('previous')">上一首</button>
<button onclick="send('next')">下一首</button>
<button class="primary wide" onclick="send('togglePlayPause')">播放 / 暂停</button>
<button class="wide" onclick="send('toggleOverlay')">显示 / 隐藏悬浮窗</button>
</section>
<div id="status" class="status">等待状态刷新</div>
</main>
<script>
const token = '{{token}}';
async function api(path, options){
  const join = path.includes('?') ? '&' : '?';
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 5000);
  const res = await fetch(path + join + 'token=' + encodeURIComponent(token), {...options, signal: controller.signal});
  clearTimeout(timeout);
  if(!res.ok) throw new Error('HTTP ' + res.status);
  return await res.json();
}
async function refresh(){
  try{
    const s = await api('/api/status');
    document.getElementById('title').textContent = s.title || '未检测到歌曲';
    document.getElementById('artist').textContent = s.artist || '请先在电脑播放媒体';
    document.getElementById('meta').textContent = (s.source || '-') + ' · ' + (s.overlayVisible ? '悬浮窗显示中' : '悬浮窗未显示');
    document.getElementById('status').textContent = '已连接，可控制电脑端当前来源。';
  }catch(e){
    document.getElementById('status').textContent = '连接失败：请确认同一 Wi-Fi、防火墙允许本程序、电脑没有切换网络。';
  }
}
async function send(action){
  try{
    await api('/api/control', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({action})});
    document.getElementById('status').textContent = '指令已发送。';
    setTimeout(refresh, 250);
  }catch(e){
    document.getElementById('status').textContent = '发送失败：' + e.message;
  }
}
refresh();
setInterval(refresh, 1500);
</script>
</body>
</html>
""";
    }

    private static string BuildLocalUrl(OverlaySettings? settings)
    {
        if (settings == null) return string.Empty;
        return $"http://127.0.0.1:{settings.RemoteControlPort}/?token={Uri.EscapeDataString(settings.RemoteControlToken)}";
    }

    private static string BuildLanUrl(OverlaySettings? settings)
    {
        if (settings == null) return string.Empty;
        return BuildLanUrls(settings).FirstOrDefault() ?? BuildLocalUrl(settings);
    }

    private static IReadOnlyList<string> BuildLanUrls(OverlaySettings? settings)
    {
        if (settings == null) return Array.Empty<string>();
        string token = Uri.EscapeDataString(settings.RemoteControlToken);
        return GetLanAddresses()
            .Select(address => $"http://{address}:{settings.RemoteControlPort}/?token={token}")
            .ToArray();
    }

    public static IReadOnlyList<string> GetLanAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .Where(adapter => adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .Select(adapter =>
                {
                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    bool hasGateway = properties.GatewayAddresses.Any(gateway =>
                        gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.Any.Equals(gateway.Address));
                    int typeScore = adapter.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet ? 1 : 0;
                    return new
                    {
                        Adapter = adapter,
                        HasGateway = hasGateway,
                        TypeScore = typeScore,
                        Addresses = properties.UnicastAddresses
                            .Select(unicast => unicast.Address)
                            .Where(RemoteControlPolicy.IsUsableLanAddress)
                            .ToArray()
                    };
                })
                .SelectMany(entry => entry.Addresses.Select(address => new
                {
                    Address = address,
                    entry.HasGateway,
                    entry.TypeScore,
                    PrivateScore = RemoteControlPolicy.IsPrivateIpv4(address) ? 1 : 0
                }))
                .OrderByDescending(entry => entry.HasGateway)
                .ThenByDescending(entry => entry.PrivateScore)
                .ThenByDescending(entry => entry.TypeScore)
                .Select(entry => entry.Address.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static OverlaySettings CloneRemoteSettings(OverlaySettings settings)
    {
        return new OverlaySettings
        {
            EnableRemoteControl = settings.EnableRemoteControl,
            RemoteControlPort = settings.RemoteControlPort,
            RemoteControlToken = settings.RemoteControlToken,
            RemoteControlAllowLan = settings.RemoteControlAllowLan
        };
    }

    private sealed record RemoteHttpRequest(
        string Method,
        string Path,
        Dictionary<string, string> Query,
        Dictionary<string, string> Headers,
        string Body);
}
