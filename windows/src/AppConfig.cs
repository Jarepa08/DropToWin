using System.Text.Json;
using System.Text.Json.Serialization;

namespace DropToWin;

sealed class TrustedDevice
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"android" (Bluetooth) o "iphone" (Wi-Fi).</summary>
    public string Kind { get; set; } = "android";
    public string? BtAddress { get; set; }
    /// <summary>SHA-256 de la llave HTTP de un iPhone (la llave en sí solo la guarda el iPhone).</summary>
    public string? TokenHash { get; set; }
    public DateTime ApprovedUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
}

/// <summary>Configuración persistente (config.json en la carpeta de datos).</summary>
sealed class AppConfig
{
    public string PcId { get; set; } = Guid.NewGuid().ToString("N");
    public string PcName { get; set; } = Environment.MachineName;
    public string ReceiveFolder { get; set; } = "";
    public bool OrganizeByType { get; set; }
    public bool WifiTurbo { get; set; } = true;
    public bool IphoneEnabled { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public bool OpenLinks { get; set; }
    public List<TrustedDevice> TrustedDevices { get; set; } = new();

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    readonly object gate = new();
    string path = "";

    /// <summary>Se dispara (desde cualquier hilo) cuando cambia la lista de dispositivos.</summary>
    public event Action? DevicesChanged;

    public static AppConfig Load(string path, string defaultFolder)
    {
        AppConfig? config = null;
        if (File.Exists(path))
        {
            try { config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)); }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Log.Error("Config", ex);
                File.Copy(path, path + ".danado", overwrite: true);
            }
        }
        config ??= new AppConfig();
        config.path = path;
        if (string.IsNullOrWhiteSpace(config.ReceiveFolder))
            config.ReceiveFolder = defaultFolder;
        if (string.IsNullOrWhiteSpace(config.PcName))
            config.PcName = Environment.MachineName;
        config.Save();
        return config;
    }

    public void Save()
    {
        lock (gate)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
    }

    public TrustedDevice? FindDevice(string deviceId)
    {
        lock (gate)
            return TrustedDevices.FirstOrDefault(d => d.DeviceId == deviceId);
    }

    public List<TrustedDevice> DevicesSnapshot()
    {
        lock (gate)
            return TrustedDevices.Select(d => new TrustedDevice
            {
                DeviceId = d.DeviceId, Name = d.Name, Kind = d.Kind, BtAddress = d.BtAddress,
                TokenHash = d.TokenHash, ApprovedUtc = d.ApprovedUtc, LastSeenUtc = d.LastSeenUtc,
            }).ToList();
    }

    /// <summary>iPhone cuya llave HTTP tiene este hash, o null.</summary>
    public TrustedDevice? FindByTokenHash(string tokenHash)
    {
        lock (gate)
            return TrustedDevices.FirstOrDefault(d => d.TokenHash is not null && d.TokenHash == tokenHash);
    }

    public void TrustIphone(string deviceId, string name, string tokenHash)
    {
        lock (gate)
        {
            TrustedDevices.Add(new TrustedDevice
            {
                DeviceId = deviceId, Name = name, Kind = "iphone", TokenHash = tokenHash,
                ApprovedUtc = DateTime.UtcNow, LastSeenUtc = DateTime.UtcNow,
            });
        }
        Save();
        DevicesChanged?.Invoke();
    }

    public void TrustDevice(string deviceId, string name, string? btAddress)
    {
        lock (gate)
        {
            TrustedDevices.RemoveAll(d => d.DeviceId == deviceId);
            TrustedDevices.Add(new TrustedDevice
            {
                DeviceId = deviceId, Name = name, Kind = "android", BtAddress = btAddress,
                ApprovedUtc = DateTime.UtcNow, LastSeenUtc = DateTime.UtcNow,
            });
        }
        Save();
        DevicesChanged?.Invoke();
    }

    public void TouchDevice(string deviceId, string name)
    {
        lock (gate)
        {
            var d = TrustedDevices.FirstOrDefault(x => x.DeviceId == deviceId);
            if (d is null) return;
            d.Name = name;
            d.LastSeenUtc = DateTime.UtcNow;
        }
        Save();
        DevicesChanged?.Invoke();
    }

    public void RemoveDevice(string deviceId)
    {
        lock (gate)
            TrustedDevices.RemoveAll(d => d.DeviceId == deviceId);
        Save();
        DevicesChanged?.Invoke();
    }

    [JsonIgnore]
    public string FilePath => path;
}
