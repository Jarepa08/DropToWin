using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DropToWin.Protocol;
using DropToWin.Transfers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace DropToWin.Server;

/// <summary>
/// Receptor para iPhone. iOS no permite transferir archivos por Bluetooth a un PC, así que el iPhone
/// envía por la red local: desde la app Atajos (menú Compartir, formulario multipart) o desde la
/// página /iphone en Safari (subida por trozos reanudable). Cada iPhone se empareja una vez con un
/// código QR de un solo uso y recibe una llave propia (revocable en la pestaña Dispositivos).
/// </summary>
sealed class IphoneServer(AppHost host, bool loopbackOnly) : IAsyncDisposable
{
    public const int DefaultPort = 47812;
    static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(10);
    const long MaxFileSize = 1L << 40;

    readonly object pairingGate = new();
    readonly Dictionary<string, byte[]> resources = new();
    WebApplication? app;
    string? pairingCode;
    DateTime pairingExpiresUtc;

    public int Port => DefaultPort;
    public bool IsListening { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Un iPhone completó el emparejamiento (se dispara desde un hilo de fondo).</summary>
    public event Action<TrustedDevice>? Paired;

    public async Task StartAsync()
    {
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.AddServerHeader = false;
                k.Limits.MaxRequestBodySize = null; // las subidas pueden ser enormes; los JSON se limitan a mano
                k.Limits.MinRequestBodyDataRate = new MinDataRate(100, TimeSpan.FromSeconds(30));
                k.RequestHeaderEncodingSelector = _ => Encoding.UTF8;
                k.Listen(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, Port);
            });
            app = builder.Build();
            MapRoutes(app);
            await app.StartAsync();
            IsListening = true;
            Log.Info("iPhone", $"Receptor HTTP en el puerto {Port}");
        }
        catch (Exception ex)
        {
            Error = ex is IOException ? $"el puerto {Port} lo usa otra aplicación" : ex.Message;
            Log.Error("iPhone", ex);
        }
    }

    /// <summary>Código de un solo uso para el QR de "Conectar un iPhone". Caduca a los 10 minutos.</summary>
    public string NewPairingCode()
    {
        lock (pairingGate)
        {
            pairingCode = NewSecret(16);
            pairingExpiresUtc = DateTime.UtcNow + PairingLifetime;
            return pairingCode;
        }
    }

    public void CancelPairing()
    {
        lock (pairingGate) pairingCode = null;
    }

    /// <summary>Direcciones que puede usar el iPhone para llegar a este PC.</summary>
    public IReadOnlyList<string> Addresses() => loopbackOnly
        ? ["127.0.0.1"]
        : LocalNetwork.GetAddresses().Select(a => a.Address.ToString()).ToList();

    void MapRoutes(WebApplication a)
    {
        a.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            ctx.Response.Headers.CacheControl = "no-store";
            if (!host.Config.IphoneEnabled)
            {
                await Json(ctx, 503, Problem("disabled", "La recepción desde iPhone está desactivada en DropToWin (Ajustes)."));
                return;
            }
            await next(ctx);
        });

        a.MapGet("/", ctx => { ctx.Response.Redirect("/iphone"); return Task.CompletedTask; });
        a.MapGet("/iphone", ctx => Resource(ctx, "iphone.html", "text/html; charset=utf-8"));
        a.MapGet("/iphone.js", ctx => Resource(ctx, "iphone.js", "text/javascript; charset=utf-8"));
        a.MapGet("/iphone.css", ctx => Resource(ctx, "iphone.css", "text/css; charset=utf-8"));
        a.MapGet("/icon.png", ctx =>
        {
            ctx.Response.ContentType = "image/png";
            return ctx.Response.Body.WriteAsync(Ui.IconArt.TouchIconPng).AsTask();
        });

        a.MapGet("/api/info", ctx => Json(ctx, 200, new JsonObject { ["app"] = "DropToWin", ["pcName"] = host.Config.PcName }));
        a.MapPost("/api/pair", PairAsync);
        a.MapGet("/api/ping", ctx => WithDevice(ctx, d => Json(ctx, 200, new JsonObject
        {
            ["pcName"] = host.Config.PcName,
            ["deviceName"] = d.Name,
        })));
        a.MapPost("/api/upload", ctx => WithDevice(ctx, d => UploadAsync(ctx, d)));
        a.MapPost("/api/text", ctx => WithDevice(ctx, d => TextAsync(ctx, d)));
        a.MapPost("/api/batch", ctx => WithDevice(ctx, d => CreateBatchAsync(ctx, d)));
        a.MapGet("/api/batch/{batchId}/{fileId}", ctx => WithDevice(ctx, d => FileStatusAsync(ctx, d)));
        a.MapPut("/api/batch/{batchId}/{fileId}", ctx => WithDevice(ctx, d => PutChunkAsync(ctx, d)));
    }

    // ───────────────────────── Emparejamiento ─────────────────────────

    async Task PairAsync(HttpContext ctx)
    {
        var body = await ReadJsonAsync(ctx, 4096);
        var code = body?.Str("code") ?? "";
        var name = CleanName(body?.Str("name")) ?? "iPhone";
        bool valid;
        lock (pairingGate)
        {
            valid = pairingCode is not null && DateTime.UtcNow < pairingExpiresUtc &&
                    CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(pairingCode));
            if (valid) pairingCode = null;
        }
        if (!valid)
        {
            await Json(ctx, 403, Problem("code",
                "El código caducó o ya se usó. En el PC abre de nuevo DropToWin › Dispositivos › Conectar un iPhone."));
            return;
        }

        var token = NewSecret(32);
        var deviceId = "ios-" + Guid.NewGuid().ToString("N")[..12];
        host.Config.TrustIphone(deviceId, name, Hash(token));
        Log.Info("iPhone", $"Emparejado «{name}»");
        var device = host.Config.FindDevice(deviceId)!;
        Paired?.Invoke(device);
        await Json(ctx, 200, new JsonObject
        {
            ["token"] = token,
            ["deviceId"] = deviceId,
            ["deviceName"] = name,
            ["pcName"] = host.Config.PcName,
            ["uploadUrl"] = $"http://{ctx.Request.Host}/api/upload",
        });
    }

    async Task WithDevice(HttpContext ctx, Func<TrustedDevice, Task> handler)
    {
        string? value = ctx.Request.Headers.Authorization;
        if (string.IsNullOrWhiteSpace(value)) value = ctx.Request.Headers["X-DTW-Token"];
        value = value?.Trim();
        if (value is not null && value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) value = value[7..].Trim();
        var device = string.IsNullOrEmpty(value) || value.Length > 200 ? null : host.Config.FindByTokenHash(Hash(value));
        if (device is not { Kind: "iphone" })
        {
            await Json(ctx, 401, Problem("token", "Este iPhone no está autorizado en DropToWin. Vuelve a conectarlo con el código QR."));
            return;
        }
        if (DateTime.UtcNow - device.LastSeenUtc > TimeSpan.FromMinutes(1))
            host.Config.TouchDevice(device.DeviceId, device.Name);
        await handler(device);
    }

    // ───────────────────────── Atajos (multipart) ─────────────────────────

    /// <summary>
    /// Formulario multipart (lo que envía «Obtener contenido de URL» con cuerpo Formulario): cada campo
    /// de tipo archivo se guarda; un campo de texto llamado "texto" va al portapapeles. También acepta un
    /// cuerpo crudo con el nombre en ?name= o en la cabecera X-DTW-Filename.
    /// </summary>
    async Task UploadAsync(HttpContext ctx, TrustedDevice device)
    {
        var saved = new JsonArray();
        var ct = ctx.RequestAborted;
        if (MediaTypeHeaderValue.TryParse(ctx.Request.ContentType, out var mediaType) &&
            mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
            if (string.IsNullOrEmpty(boundary))
            {
                await Json(ctx, 400, Problem("invalid", "Formulario sin delimitador"));
                return;
            }
            var reader = new MultipartReader(boundary, ctx.Request.Body) { BodyLengthLimit = null, HeadersLengthLimit = 16 * 1024 };
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)) continue;
                var fileName = disposition.FileNameStar.HasValue
                    ? disposition.FileNameStar.Value
                    : HeaderUtilities.RemoveQuotes(disposition.FileName).Value;
                if (string.IsNullOrEmpty(fileName))
                {
                    var field = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? "";
                    if (field is "texto" or "text")
                    {
                        var text = await ReadLimitedStringAsync(section.Body, 1 << 20, ct);
                        if (!string.IsNullOrWhiteSpace(text)) host.OnTextReceived(device.Name, text, "wifi");
                        saved.Add("(texto)");
                    }
                    continue;
                }
                saved.Add(await SaveAsync(section.Body, fileName, section.ContentType, null, device, ct));
            }
        }
        else
        {
            var name = ctx.Request.Query["name"].FirstOrDefault();
            if (string.IsNullOrEmpty(name)) name = Uri.UnescapeDataString(ctx.Request.Headers["X-DTW-Filename"].FirstOrDefault() ?? "");
            if (string.IsNullOrEmpty(name)) name = "archivo";
            saved.Add(await SaveAsync(ctx.Request.Body, name, ctx.Request.ContentType, null, device, ct));
        }

        if (saved.Count == 0)
        {
            await Json(ctx, 400, Problem("empty", "No llegó ningún archivo. Revisa que el campo del formulario sea de tipo Archivo."));
            return;
        }
        await Json(ctx, 200, new JsonObject { ["saved"] = saved });
    }

    async Task<string> SaveAsync(Stream body, string fileName, string? mime, long? modifiedMs, TrustedDevice device, CancellationToken ct)
    {
        var temp = host.Transfers.NewTempPath();
        try
        {
            await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
                await body.CopyToAsync(fs, 1 << 20, ct);
            if (Path.GetExtension(fileName).Length == 0 && ExtensionFor(mime) is string ext) fileName += ext;
            var path = host.Transfers.ImportFile(device.DeviceId, device.Name, "wifi", fileName, mime, modifiedMs, temp);
            return Path.GetFileName(path);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { }
            throw;
        }
    }

    async Task TextAsync(HttpContext ctx, TrustedDevice device)
    {
        var body = await ReadJsonAsync(ctx, 1 << 20);
        var text = body?.Str("text");
        if (string.IsNullOrWhiteSpace(text))
        {
            await Json(ctx, 400, Problem("invalid", "Texto vacío"));
            return;
        }
        host.OnTextReceived(device.Name, text, "wifi");
        await Json(ctx, 200, new JsonObject { ["ok"] = true });
    }

    // ───────────────────────── Página de Safari (por trozos, reanudable) ─────────────────────────

    async Task CreateBatchAsync(HttpContext ctx, TrustedDevice device)
    {
        var body = await ReadJsonAsync(ctx, 2 << 20);
        if (body?["files"] is not JsonArray array || array.Count == 0 || array.Count > 10_000)
        {
            await Json(ctx, 400, Problem("invalid", "Lista de archivos inválida"));
            return;
        }
        var specs = new List<FileSpec>();
        foreach (var node in array)
        {
            if (node is not JsonObject f || f.Str("name") is not string name ||
                f.Long("size") is not long size || size < 0 || size > MaxFileSize)
            {
                await Json(ctx, 400, Problem("invalid", "Descripción de archivo inválida"));
                return;
            }
            specs.Add(new FileSpec { Name = name, Size = size, Mime = f.Str("mime"), ModifiedMs = f.Long("modified") });
        }
        try
        {
            var batch = host.Transfers.Create(device.DeviceId, device.Name, "wifi", specs);
            var files = new JsonArray();
            foreach (var f in batch.Files) files.Add(new JsonObject { ["id"] = f.Id, ["done"] = f.Done });
            await Json(ctx, 200, new JsonObject { ["batchId"] = batch.Id, ["files"] = files });
        }
        catch (InsufficientSpaceException ex)
        {
            await Json(ctx, 507, Problem("no-space", ex.Message));
        }
    }

    async Task FileStatusAsync(HttpContext ctx, TrustedDevice device)
    {
        var (_, file) = await FindFileAsync(ctx, device);
        if (file is null) return;
        await Json(ctx, 200, FileState(file));
    }

    async Task PutChunkAsync(HttpContext ctx, TrustedDevice device)
    {
        var (batch, file) = await FindFileAsync(ctx, device);
        if (batch is null || file is null) return;
        if (file.Done)
        {
            await Json(ctx, 200, FileState(file));
            return;
        }
        if (!long.TryParse(ctx.Request.Query["offset"], out var offset) || offset < 0)
        {
            await Json(ctx, 400, Problem("invalid", "Falta la posición (offset)"));
            return;
        }

        var ct = ctx.RequestAborted;
        await file.Gate.WaitAsync(ct);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
        FileStream? fs = null;
        try
        {
            fs = new FileStream(file.PartPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, 1, FileOptions.Asynchronous);
            if (fs.Length > file.Size) fs.SetLength(0);
            if (offset != fs.Length)
            {
                await Json(ctx, 409, new JsonObject { ["received"] = fs.Length, ["done"] = false });
                return;
            }
            fs.Position = offset;
            long remaining = file.Size - offset;
            int n;
            while ((n = await ctx.Request.Body.ReadAsync(buffer, ct)) > 0)
            {
                if (n > remaining)
                {
                    await Json(ctx, 400, Problem("invalid", "Se recibieron más datos de los anunciados"));
                    return;
                }
                await fs.WriteAsync(buffer.AsMemory(0, n), CancellationToken.None);
                remaining -= n;
                Interlocked.Add(ref file.Received, n);
                host.Transfers.ReportProgress(batch, file);
            }
            await fs.FlushAsync(CancellationToken.None);
            Interlocked.Exchange(ref file.Received, fs.Length);
            bool complete = fs.Length == file.Size;
            await fs.DisposeAsync();
            fs = null;
            if (complete) host.Transfers.Complete(batch, file);
            await Json(ctx, 200, FileState(file));
        }
        finally
        {
            if (fs is not null)
            {
                try
                {
                    await fs.FlushAsync(CancellationToken.None);
                    Interlocked.Exchange(ref file.Received, fs.Length);
                    await fs.DisposeAsync();
                }
                catch (IOException) { }
            }
            ArrayPool<byte>.Shared.Return(buffer);
            file.Gate.Release();
        }
    }

    async Task<(Batch? Batch, IncomingFile? File)> FindFileAsync(HttpContext ctx, TrustedDevice device)
    {
        var batch = host.Transfers.Find(device.DeviceId, ctx.Request.RouteValues["batchId"] as string ?? "");
        var file = batch?.Files.FirstOrDefault(f => f.Id == ctx.Request.RouteValues["fileId"] as string);
        if (file is null)
            await Json(ctx, 404, Problem("unknown-batch", "El PC no reconoce esta transferencia (¿se reinició?)."));
        return (batch, file);
    }

    static JsonObject FileState(IncomingFile file) => new()
    {
        ["received"] = Interlocked.Read(ref file.Received),
        ["done"] = file.Done,
        ["savedAs"] = file.Done ? Path.GetFileName(file.SavedPath) : null,
    };

    // ───────────────────────── Utilidades ─────────────────────────

    async Task Resource(HttpContext ctx, string name, string contentType)
    {
        byte[] bytes;
        lock (resources)
        {
            if (!resources.TryGetValue(name, out bytes!))
            {
                using var stream = typeof(IphoneServer).Assembly.GetManifestResourceStream("DropToWin.web." + name)!;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                bytes = resources[name] = ms.ToArray();
            }
        }
        ctx.Response.ContentType = contentType;
        await ctx.Response.Body.WriteAsync(bytes);
    }

    static async Task<JsonObject?> ReadJsonAsync(HttpContext ctx, int maxBytes)
    {
        var text = await ReadLimitedStringAsync(ctx.Request.Body, maxBytes, ctx.RequestAborted);
        try { return JsonNode.Parse(text) as JsonObject; }
        catch (JsonException) { return null; }
    }

    static async Task<string> ReadLimitedStringAsync(Stream body, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int n;
        while ((n = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + n > maxBytes) throw new Microsoft.AspNetCore.Http.BadHttpRequestException("Cuerpo demasiado grande", 413);
            ms.Write(buffer, 0, n);
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    static Task Json(HttpContext ctx, int status, JsonObject body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        return ctx.Response.WriteAsync(body.ToJsonString());
    }

    static JsonObject Problem(string code, string message) => new() { ["error"] = code, ["message"] = message };

    static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }

    static string? ExtensionFor(string? mime) => mime?.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/heic" => ".heic",
        "image/heif" => ".heif",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "video/quicktime" => ".mov",
        "video/mp4" => ".mp4",
        "audio/mpeg" => ".mp3",
        "audio/x-m4a" or "audio/mp4" => ".m4a",
        "application/pdf" => ".pdf",
        "text/plain" => ".txt",
        _ => null,
    };

    static string NewSecret(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async ValueTask DisposeAsync()
    {
        IsListening = false;
        if (app is not null) await app.DisposeAsync();
    }
}
