using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

namespace DropToWin.Transfers;

sealed class FileSpec
{
    public required string Name { get; init; }
    public required long Size { get; init; }
    public string? Mime { get; init; }
    public long? ModifiedMs { get; init; }
}

sealed class IncomingFile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required long Size { get; init; }
    public string? Mime { get; init; }
    public long? ModifiedMs { get; init; }
    public required string PartPath { get; init; }

    public long Received;
    public volatile bool Done;
    public string? SavedPath;

    /// <summary>Solo una sesión escribe el archivo a la vez.</summary>
    public readonly SemaphoreSlim Gate = new(1, 1);
    /// <summary>Permite que una sesión nueva desplace a otra que quedó colgada.</summary>
    public CancellationTokenSource? Writer;
}

sealed class Batch
{
    public required string Id { get; init; }
    public required string DeviceId { get; init; }
    public required string Sender { get; init; }
    public required List<IncomingFile> Files { get; init; }
    public string Transport { get; set; } = "";
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

    public long TotalBytes => Files.Sum(f => f.Size);
    public long ReceivedBytes => Files.Sum(f => Interlocked.Read(ref f.Received));

    internal long LastReportTicks;
    internal long LastReportBytes;
    internal double Speed;
}

sealed record BatchProgress(
    string BatchId, string Sender, string Transport, string FileName,
    int FileIndex, int FileCount, long Received, long Total, double BytesPerSecond, bool Completed);

static class CancellationExtensions
{
    /// <summary>Cancela sin fallar si otra sesión ya liberó el token.</summary>
    public static void TryCancel(this CancellationTokenSource? cts)
    {
        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}

sealed class InsufficientSpaceException(long needed, long available)
    : IOException($"Espacio insuficiente en el PC: se necesitan {Format.Size(needed)} y hay {Format.Size(available)} libres.");

/// <summary>Lotes de archivos en curso, archivos parciales y finalización en la carpeta de destino.</summary>
sealed class TransferStore : IDisposable
{
    readonly AppConfig config;
    readonly ConcurrentDictionary<string, Batch> batches = new();
    readonly System.Threading.Timer cleanupTimer;

    public event Action<Batch, IncomingFile>? FileCompleted;
    public event Action<Batch>? BatchCompleted;
    public event Action<BatchProgress>? Progress;

    public TransferStore(AppConfig config)
    {
        this.config = config;
        cleanupTimer = new System.Threading.Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        Task.Run(CleanStalePartials);
    }

    string PartialDir => Path.Combine(config.ReceiveFolder, ".droptowin-partial");

    public Batch Create(string deviceId, string sender, string transport, IReadOnlyList<FileSpec> specs)
    {
        var dir = PartialDir;
        var info = Directory.CreateDirectory(dir);
        info.Attributes |= FileAttributes.Hidden;
        CheckFreeSpace(dir, specs.Sum(s => s.Size));

        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var batch = new Batch
        {
            Id = id,
            DeviceId = deviceId,
            Sender = sender,
            Transport = transport,
            Files = specs.Select((s, i) => new IncomingFile
            {
                Id = i.ToString(),
                Name = FileNames.Sanitize(s.Name),
                Size = s.Size,
                Mime = s.Mime,
                ModifiedMs = s.ModifiedMs,
                PartPath = Path.Combine(dir, $"{id}-{i}.part"),
            }).ToList(),
        };
        batches[id] = batch;

        foreach (var f in batch.Files.Where(f => f.Size == 0))
        {
            File.WriteAllBytes(f.PartPath, []);
            Complete(batch, f);
        }
        return batch;
    }

    /// <summary>Ruta temporal (en la misma unidad que el destino) para recibir un archivo de tamaño desconocido.</summary>
    public string NewTempPath()
    {
        var info = Directory.CreateDirectory(PartialDir);
        info.Attributes |= FileAttributes.Hidden;
        return Path.Combine(info.FullName, $"subida-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}.tmp");
    }

    /// <summary>Guarda un archivo ya recibido entero (subidas HTTP de una sola vez, p. ej. desde Atajos de iOS).</summary>
    public string ImportFile(string deviceId, string sender, string transport, string name, string? mime, long? modifiedMs, string tempPath)
    {
        var size = new FileInfo(tempPath).Length;
        var batch = Create(deviceId, sender, transport,
            [new FileSpec { Name = name, Size = size, Mime = mime, ModifiedMs = modifiedMs }]);
        var file = batch.Files[0];
        if (file.Done)
        {
            File.Delete(tempPath); // Archivo vacío: Create ya lo guardó.
            return file.SavedPath!;
        }
        File.Move(tempPath, file.PartPath, overwrite: true);
        return Complete(batch, file);
    }

    public Batch? Find(string deviceId, string batchId) =>
        batches.TryGetValue(batchId, out var b) && b.DeviceId == deviceId ? b : null;

    /// <summary>Mueve el archivo parcial a su destino final y avisa a la interfaz.</summary>
    public string Complete(Batch batch, IncomingFile file)
    {
        bool batchDone;
        lock (batch)
        {
            if (file.Done) return file.SavedPath!;
            var folder = config.ReceiveFolder;
            if (config.OrganizeByType)
                folder = Path.Combine(folder, FileNames.Category(file.Name, file.Mime));
            Directory.CreateDirectory(folder);
            var saved = FileNames.MoveToUnique(file.PartPath, folder, file.Name);
            if (file.ModifiedMs is long ms && ms > 0)
            {
                try { File.SetLastWriteTimeUtc(saved, DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime); }
                catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or UnauthorizedAccessException) { }
            }
            file.SavedPath = saved;
            Interlocked.Exchange(ref file.Received, file.Size);
            file.Done = true;
            batch.LastActivityUtc = DateTime.UtcNow;
            batchDone = batch.Files.All(f => f.Done);
        }

        FileCompleted?.Invoke(batch, file);
        if (batchDone && batches.TryRemove(batch.Id, out _))
        {
            Progress?.Invoke(Snapshot(batch, file, completed: true));
            BatchCompleted?.Invoke(batch);
        }
        return file.SavedPath!;
    }

    /// <summary>Llamado por cada trama recibida; limita los avisos a ~4 por segundo.</summary>
    public void ReportProgress(Batch batch, IncomingFile file)
    {
        long now = Stopwatch.GetTimestamp();
        long last = Interlocked.Read(ref batch.LastReportTicks);
        double elapsed = (now - last) / (double)Stopwatch.Frequency;
        if (last != 0 && elapsed < 0.25) return;
        if (Interlocked.CompareExchange(ref batch.LastReportTicks, now, last) != last) return;

        long received = batch.ReceivedBytes;
        if (last != 0)
        {
            double instant = Math.Max(0, received - batch.LastReportBytes) / elapsed;
            batch.Speed = batch.Speed == 0 ? instant : batch.Speed * 0.7 + instant * 0.3;
        }
        batch.LastReportBytes = received;
        batch.LastActivityUtc = DateTime.UtcNow;
        Progress?.Invoke(Snapshot(batch, file, completed: false));
    }

    BatchProgress Snapshot(Batch batch, IncomingFile file, bool completed) => new(
        batch.Id, batch.Sender, batch.Transport, file.Name,
        batch.Files.IndexOf(file), batch.Files.Count,
        batch.ReceivedBytes, batch.TotalBytes, batch.Speed, completed);

    public bool Cancel(string deviceId, string batchId)
    {
        if (Find(deviceId, batchId) is null || !batches.TryRemove(batchId, out var batch)) return false;
        foreach (var f in batch.Files.Where(f => !f.Done))
        {
            f.Writer.TryCancel();
            TryDelete(f.PartPath);
        }
        return true;
    }

    void Cleanup()
    {
        var limit = DateTime.UtcNow.AddHours(-12);
        foreach (var b in batches.Values.Where(b => b.LastActivityUtc < limit).ToList())
        {
            if (!batches.TryRemove(b.Id, out _)) continue;
            foreach (var f in b.Files.Where(f => !f.Done)) TryDelete(f.PartPath);
        }
    }

    void CleanStalePartials()
    {
        try
        {
            var dir = PartialDir;
            if (!Directory.Exists(dir)) return;
            var limit = DateTime.UtcNow.AddDays(-2);
            foreach (var f in Directory.EnumerateFiles(dir, "*.part"))
                if (File.GetLastWriteTimeUtc(f) < limit) TryDelete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static void CheckFreeSpace(string dir, long needed)
    {
        long available;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dir));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return;
            available = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return;
        }
        if (needed + 50L * 1024 * 1024 > available)
            throw new InsufficientSpaceException(needed, available);
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => cleanupTimer.Dispose();
}
