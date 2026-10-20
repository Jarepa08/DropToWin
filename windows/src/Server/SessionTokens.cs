using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DropToWin.Server;

/// <summary>
/// Llaves de un solo dispositivo para el Wi-Fi turbo. Se entregan por Bluetooth (canal ya
/// autenticado) y caducan 30 minutos después de su último uso.
/// </summary>
sealed class SessionTokens
{
    static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    readonly ConcurrentDictionary<string, (string DeviceId, DateTime Expires)> tokens = new();

    public string Issue(string deviceId)
    {
        Prune();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        tokens[Hash(token)] = (deviceId, DateTime.UtcNow + Lifetime);
        return token;
    }

    public bool Validate(string token, string deviceId)
    {
        var key = Hash(token);
        if (!tokens.TryGetValue(key, out var entry) || entry.DeviceId != deviceId || entry.Expires < DateTime.UtcNow)
            return false;
        tokens[key] = (deviceId, DateTime.UtcNow + Lifetime);
        return true;
    }

    public void RevokeDevice(string deviceId)
    {
        foreach (var pair in tokens.Where(p => p.Value.DeviceId == deviceId).ToList())
            tokens.TryRemove(pair.Key, out _);
    }

    void Prune()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in tokens.Where(p => p.Value.Expires < now).ToList())
            tokens.TryRemove(pair.Key, out _);
    }

    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
