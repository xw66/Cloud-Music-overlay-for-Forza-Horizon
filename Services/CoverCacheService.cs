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

    public CoverCacheService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _diskCacheDir = Path.Combine(appData, "HorizonRadioOverlay", "covers");
    }

    public byte[]? TryGet(string key)
    {
        if (_cache.TryGetValue(key, out byte[]? cached))
        {
            TouchKey(key);
            return cached;
        }

        byte[]? disk = TryReadFromDisk(key);
        if (disk != null)
        {
            PutMemory(key, disk);
        }

        return disk;
    }

    public void Set(string key, byte[]? data)
    {
        if (data == null || data.Length == 0) return;

        PutMemory(key, data);
        WriteToDisk(key, data);
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

    private byte[]? TryReadFromDisk(string key)
    {
        try
        {
            string path = Path.Combine(_diskCacheDir, HashKey(key) + ".bin");
            if (!File.Exists(path)) return null;
            return File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }

    private void WriteToDisk(string key, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(_diskCacheDir);
            string path = Path.Combine(_diskCacheDir, HashKey(key) + ".bin");
            File.WriteAllBytes(path, data);
        }
        catch
        {
        }
    }
}
