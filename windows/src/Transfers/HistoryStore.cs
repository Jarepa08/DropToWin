using System.Text.Json;

namespace DropToWin.Transfers;

sealed class HistoryItem
{
    public string Kind { get; set; } = "file";   // "file" | "text"
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public string? Text { get; set; }
    public long Size { get; set; }
    public string Sender { get; set; } = "";
    public string Transport { get; set; } = "";
    public DateTime TimeUtc { get; set; }

    public bool IsText => Kind == "text";
}

/// <summary>Últimos elementos recibidos (historial.json).</summary>
sealed class HistoryStore
{
    const int MaxItems = 500;
    readonly string path;
    readonly object gate = new();
    readonly List<HistoryItem> items;

    public event Action<HistoryItem>? Added;

    public HistoryStore(string path)
    {
        this.path = path;
        items = new List<HistoryItem>();
        if (File.Exists(path))
        {
            try { items = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(path)) ?? new(); }
            catch (Exception ex) when (ex is JsonException or IOException) { Log.Error("Historial", ex); }
        }
    }

    public List<HistoryItem> Snapshot()
    {
        lock (gate) return items.ToList();
    }

    public void Add(HistoryItem item)
    {
        lock (gate)
        {
            items.Add(item);
            if (items.Count > MaxItems) items.RemoveRange(0, items.Count - MaxItems);
            SaveLocked();
        }
        Added?.Invoke(item);
    }

    public void Remove(HistoryItem item)
    {
        lock (gate)
        {
            items.Remove(item);
            SaveLocked();
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            items.Clear();
            SaveLocked();
        }
    }

    void SaveLocked()
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(items));
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException ex) { Log.Error("Historial", ex); }
    }
}
