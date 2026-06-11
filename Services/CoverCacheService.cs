using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HorizonRadioOverlay.Services;

public sealed class CoverCacheService
{
    private const int MemoryCapacity = 80;
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private readonly string _diskCacheDir;
    private readonly DiagnosticService? _diagnostic;

    public CoverCacheService(DiagnosticService? diagnostic = null)
    {
        _diagnostic = diagnostic;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _diskCacheDir = Path.Combine(appData, "HorizonRadioOverlay", "covers");
    }

    public byte[]? TryGet(string key, string? traceId = null)
    {
        if (_cache.TryGetValue(key, out byte[]? cached))
        {
            TouchKey(key);
            Log(traceId, "cache-memory", ("status", "hit"), ("key", key), ("bytes", cached.Length));
            return cached;
        }

        Log(traceId, "cache-memory", ("status", "miss"), ("key", key));
        byte[]? disk = TryReadFromDisk(key, traceId);
        if (disk != null)
        {
            PutMemory(key, disk);
        }

        return disk;
    }

    public void Set(string key, byte[]? data, string? traceId = null)
    {
        if (data == null || data.Length == 0) return;

        PutMemory(key, data);
        WriteToDisk(key, data, traceId);
    }

    private void PutMemory(string key, byte[] data)
    {
        if (_cache.ContainsKey(key))
        {
            _cache[key] = data;
            TouchKey(key);
            return;
        }

        while (_cache.Count >= MemoryCapacity)
        {
            string oldest = _order.Last!.Value;
            _order.RemoveLast();
            _cache.Remove(oldest);
        }

        _cache[key] = data;
        _order.AddFirst(key);
    }

    private void TouchKey(string key)
    {
        if (_order.First?.Value == key) return;
        _order.Remove(key);
        _order.AddFirst(key);
    }

    private static string HashKey(string key)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(key);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    private byte[]? TryReadFromDisk(string key, string? traceId)
    {
        try
        {
            string path = Path.Combine(_diskCacheDir, HashKey(key) + ".bin");
            if (!File.Exists(path))
            {
                Log(traceId, "cache-disk", ("status", "missing"), ("key", key), ("path", path));
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);
            Log(traceId, "cache-disk", ("status", "hit"), ("key", key), ("path", path), ("bytes", bytes.Length));
            return bytes;
        }
        catch (Exception ex)
        {
            string rootCause = DiagnosticContext.ClassifyException(ex);
            Log(traceId, "cache-disk", ("status", "read-failed"), ("rootCause", rootCause), ("key", key),
                ("error", $"{ex.GetType().Name}: {ex.Message}"), ("suggestion", DiagnosticContext.GetSuggestion(rootCause)));
            return null;
        }
    }

    private void WriteToDisk(string key, byte[] data, string? traceId)
    {
        try
        {
            Directory.CreateDirectory(_diskCacheDir);
            string path = Path.Combine(_diskCacheDir, HashKey(key) + ".bin");
            File.WriteAllBytes(path, data);
            Log(traceId, "cache-disk", ("status", "write-ok"), ("key", key), ("path", path), ("bytes", data.Length));
        }
        catch (Exception ex)
        {
            string rootCause = DiagnosticContext.ClassifyException(ex);
            Log(traceId, "cache-disk", ("status", "write-failed"), ("rootCause", rootCause), ("key", key),
                ("error", $"{ex.GetType().Name}: {ex.Message}"), ("suggestion", DiagnosticContext.GetSuggestion(rootCause)));
        }
    }

    private void Log(string? traceId, string stage, params (string Key, object? Value)[] fields)
    {
        if (_diagnostic == null || string.IsNullOrWhiteSpace(traceId))
        {
            return;
        }

        _diagnostic.Info(DiagnosticContext.Format(traceId, "netease-cover", stage, fields));
    }
}
