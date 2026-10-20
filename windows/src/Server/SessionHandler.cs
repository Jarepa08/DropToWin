using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text.Json.Nodes;
using DropToWin.Protocol;
using DropToWin.Transfers;

namespace DropToWin.Server;

/// <summary>Quién está al otro lado de una conexión.</summary>
sealed record PeerInfo(string Transport, string DisplayName, string? BtAddress, bool? IsPaired);

sealed record ApprovalRequest(string DeviceName, PeerInfo Peer);

enum ApprovalResult { Approved, Rejected, TimedOut }

sealed class DiskException(string message, Exception inner) : IOException(message, inner);

/// <summary>
/// Atiende una sesión del protocolo DropToWin sobre cualquier flujo (Bluetooth RFCOMM,
/// TLS por Wi-Fi o el transporte de prueba). Ver PROTOCOLO.md.
/// </summary>
sealed class SessionHandler(AppHost host)
{
    static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(20);
    static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(3);
    static readonly TimeSpan ApprovalTimeout = TimeSpan.FromSeconds(60);
    const int MaxFilesPerBatch = 10_000;
    const long MaxFileSize = 1L << 40;

    readonly SemaphoreSlim approvalGate = new(1, 1);

    public async Task RunAsync(Stream stream, PeerInfo peer, CancellationToken appToken)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(appToken);
        // Algunos flujos (WinRT) no respetan la cancelación: cerrarlos desbloquea la lectura.
        using var closeOnCancel = idle.Token.Register(() => SafeDispose(stream));
        var io = new FrameIO(stream);
        string who = peer.DisplayName;
        try
        {
            idle.CancelAfter(HelloTimeout);
            var hello = await io.ReadJsonAsync(idle.Token);
            var device = await HandshakeAsync(io, hello, peer, idle);
            if (device is null) return;
            who = device.Value.Name;
            Log.Info("Sesión", $"{who} conectado por {peer.Transport}");

            while (true)
            {
                idle.CancelAfter(IdleTimeout);
                var msg = await io.ReadJsonAsync(idle.Token);
                switch (msg.Str("t"))
                {
                    case "ping":
                        await io.WriteJsonAsync(new JsonObject { ["t"] = "pong" }, idle.Token);
                        break;
                    case "text":
                        await HandleTextAsync(io, msg, device.Value.Name, peer, idle.Token);
                        break;
                    case "batch":
                        await HandleBatchAsync(io, msg, device.Value.Id, device.Value.Name, peer, idle.Token);
                        break;
                    case "file":
                        await HandleFileAsync(io, stream, msg, device.Value.Id, peer, idle);
                        break;
                    case "cancel":
                        host.Transfers.Cancel(device.Value.Id, msg.Str("batchId") ?? "");
                        await io.WriteJsonAsync(new JsonObject { ["t"] = "ok" }, idle.Token);
                        break;
                    case "bye":
                        return;
                    default:
                        await SendErrorAsync(io, "invalid", "Mensaje desconocido", idle.Token);
                        break;
                }
            }
        }
        catch (Exception ex) when (IsDisconnect(ex))
        {
            Log.Info("Sesión", $"{who} ({peer.Transport}) desconectado: {ex.GetType().Name} {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Error("Sesión", ex);
        }
        finally
        {
            SafeDispose(stream);
        }
    }

    async Task<(string Id, string Name)?> HandshakeAsync(FrameIO io, JsonObject hello, PeerInfo peer, CancellationTokenSource idle)
    {
        if (hello.Str("t") != "hello" || hello.Long("v") != 1)
        {
            await DenyAsync(io, "version", "Versión de DropToWin incompatible. Actualiza ambas apps.", idle.Token);
            return null;
        }
        var deviceId = hello.Str("deviceId");
        var deviceName = Clean(hello.Str("deviceName")) ?? peer.DisplayName;
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 64)
        {
            await DenyAsync(io, "invalid", "Identificador de dispositivo inválido.", idle.Token);
            return null;
        }

        if (peer.Transport == "wifi")
        {
            var token = hello.Str("token");
            if (token is null || host.Config.FindDevice(deviceId) is null || !host.Tokens.Validate(token, deviceId))
            {
                await DenyAsync(io, "token", "Llave de Wi-Fi no válida.", idle.Token);
                return null;
            }
        }
        else
        {
            if (peer.IsPaired == false)
            {
                await DenyAsync(io, "not-paired", "Empareja primero el teléfono con el PC desde los ajustes de Bluetooth.", idle.Token);
                return null;
            }
            var trusted = host.Config.FindDevice(deviceId);
            bool known = trusted is not null &&
                (trusted.BtAddress is null || peer.BtAddress is null ||
                 string.Equals(trusted.BtAddress, peer.BtAddress, StringComparison.OrdinalIgnoreCase));
            if (!known)
            {
                if (!await approvalGate.WaitAsync(0))
                {
                    await DenyAsync(io, "busy", "El PC está atendiendo otra solicitud. Inténtalo de nuevo.", idle.Token);
                    return null;
                }
                ApprovalResult result;
                try
                {
                    await io.WriteJsonAsync(new JsonObject { ["t"] = "pending", ["reason"] = "approval" }, idle.Token);
                    idle.CancelAfter(Timeout.InfiniteTimeSpan);
                    result = await host.RequestApprovalAsync(new ApprovalRequest(deviceName, peer), ApprovalTimeout, idle.Token);
                }
                finally
                {
                    approvalGate.Release();
                }
                idle.CancelAfter(IdleTimeout);
                if (result != ApprovalResult.Approved)
                {
                    await DenyAsync(io,
                        result == ApprovalResult.TimedOut ? "timeout" : "rejected",
                        result == ApprovalResult.TimedOut
                            ? "Nadie aceptó la conexión en el PC a tiempo."
                            : "El PC rechazó la conexión.",
                        idle.Token);
                    return null;
                }
                host.Config.TrustDevice(deviceId, deviceName, peer.BtAddress);
            }
        }
        host.Config.TouchDevice(deviceId, deviceName);

        var welcome = new JsonObject
        {
            ["t"] = "welcome",
            ["v"] = 1,
            ["pcId"] = host.Config.PcId,
            ["pcName"] = host.Config.PcName,
            ["transport"] = peer.Transport,
        };
        if (peer.Transport != "wifi" && host.Config.WifiTurbo && host.Wifi.IsListening)
        {
            var addrs = host.Wifi.OfferAddresses();
            if (addrs.Count > 0)
            {
                welcome["wifi"] = new JsonObject
                {
                    ["port"] = host.Wifi.Port,
                    ["fp"] = host.Fingerprint,
                    ["token"] = host.Tokens.Issue(deviceId),
                    ["addrs"] = new JsonArray(addrs.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
                };
            }
        }
        await io.WriteJsonAsync(welcome, idle.Token);
        return (deviceId, deviceName);
    }

    async Task HandleTextAsync(FrameIO io, JsonObject msg, string sender, PeerInfo peer, CancellationToken ct)
    {
        var text = msg.Str("text");
        if (string.IsNullOrEmpty(text))
        {
            await SendErrorAsync(io, "invalid", "Texto vacío", ct);
            return;
        }
        host.OnTextReceived(sender, text, peer.Transport);
        await io.WriteJsonAsync(new JsonObject { ["t"] = "ok" }, ct);
    }

    async Task HandleBatchAsync(FrameIO io, JsonObject msg, string deviceId, string sender, PeerInfo peer, CancellationToken ct)
    {
        if (msg["files"] is not JsonArray array || array.Count == 0 || array.Count > MaxFilesPerBatch)
        {
            await SendErrorAsync(io, "invalid", "Lista de archivos inválida", ct);
            return;
        }
        var specs = new List<FileSpec>(array.Count);
        foreach (var node in array)
        {
            if (node is not JsonObject f || f.Str("name") is not string name ||
                f.Long("size") is not long size || size < 0 || size > MaxFileSize)
            {
                await SendErrorAsync(io, "invalid", "Descripción de archivo inválida", ct);
                return;
            }
            specs.Add(new FileSpec { Name = name, Size = size, Mime = f.Str("mime"), ModifiedMs = f.Long("modified") });
        }

        Batch batch;
        try
        {
            batch = host.Transfers.Create(deviceId, sender, peer.Transport, specs);
        }
        catch (InsufficientSpaceException ex)
        {
            await SendErrorAsync(io, "no-space", ex.Message, ct);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Lote", ex);
            await SendErrorAsync(io, "disk", "No se pudo escribir en la carpeta de recepción del PC: " + ex.Message, ct);
            return;
        }

        var files = new JsonArray();
        foreach (var f in batch.Files)
            files.Add(new JsonObject { ["id"] = f.Id, ["done"] = f.Done });
        await io.WriteJsonAsync(new JsonObject { ["t"] = "batch-ok", ["batchId"] = batch.Id, ["files"] = files }, ct);
    }

    async Task HandleFileAsync(FrameIO io, Stream stream, JsonObject msg, string deviceId, PeerInfo peer, CancellationTokenSource idle)
    {
        var batch = host.Transfers.Find(deviceId, msg.Str("batchId") ?? "");
        var file = batch?.Files.FirstOrDefault(f => f.Id == msg.Str("fileId"));
        if (batch is null || file is null)
        {
            await SendErrorAsync(io, "unknown-batch", "El PC no reconoce esta transferencia (¿se reinició?).", idle.Token);
            return;
        }
        if (file.Done)
        {
            await SendFileDoneAsync(io, file, idle.Token);
            return;
        }

        // Si otra sesión (p. ej. una conexión Bluetooth que quedó colgada) escribe este archivo, se la desplaza.
        using var writer = new CancellationTokenSource();
        using var closeOnTakeover = writer.Token.Register(() => SafeDispose(stream));
        Interlocked.Exchange(ref file.Writer, writer).TryCancel();
        await file.Gate.WaitAsync(idle.Token);
        FileStream? fs = null;
        try
        {
            if (file.Done)
            {
                await SendFileDoneAsync(io, file, idle.Token);
                return;
            }
            batch.Transport = peer.Transport;
            try
            {
                fs = new FileStream(file.PartPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, 1, FileOptions.Asynchronous);
                if (fs.Length > file.Size) fs.SetLength(0);
                fs.Position = fs.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await SendErrorAsync(io, "disk", "No se pudo escribir en el PC: " + ex.Message, idle.Token);
                return;
            }
            long offset = fs.Length;
            Interlocked.Exchange(ref file.Received, offset);
            await io.WriteJsonAsync(new JsonObject { ["t"] = "file-ready", ["offset"] = offset }, idle.Token);

            long remaining = file.Size - offset;
            while (remaining > 0)
            {
                idle.CancelAfter(IdleTimeout);
                var frame = await io.ReadAsync(idle.Token);
                if (frame.Kind != FrameIO.KindData)
                    throw new ProtocolException("Se esperaban datos del archivo");
                if (frame.Payload.Length > remaining)
                    throw new ProtocolException("Se recibieron más datos de los anunciados");
                try
                {
                    await fs.WriteAsync(frame.Payload, CancellationToken.None);
                }
                catch (IOException ex)
                {
                    throw new DiskException("Error al escribir en el disco del PC: " + ex.Message, ex);
                }
                remaining -= frame.Payload.Length;
                Interlocked.Add(ref file.Received, frame.Payload.Length);
                host.Transfers.ReportProgress(batch, file);
            }
            await fs.FlushAsync(CancellationToken.None);
            await fs.DisposeAsync();
            fs = null;

            try
            {
                host.Transfers.Complete(batch, file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new DiskException("No se pudo guardar el archivo en el PC: " + ex.Message, ex);
            }
            await SendFileDoneAsync(io, file, idle.Token);
        }
        catch (DiskException ex)
        {
            Log.Error("Archivo", ex);
            try { await SendErrorAsync(io, "disk", ex.Message, idle.Token); } catch (Exception e) when (IsDisconnect(e)) { }
            throw;
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
            Interlocked.CompareExchange(ref file.Writer, null, writer);
            file.Gate.Release();
        }
    }

    static Task SendFileDoneAsync(FrameIO io, IncomingFile file, CancellationToken ct) =>
        io.WriteJsonAsync(new JsonObject { ["t"] = "file-done", ["savedAs"] = Path.GetFileName(file.SavedPath) }, ct);

    static Task SendErrorAsync(FrameIO io, string code, string message, CancellationToken ct) =>
        io.WriteJsonAsync(new JsonObject { ["t"] = "error", ["code"] = code, ["message"] = message }, ct);

    static Task DenyAsync(FrameIO io, string reason, string message, CancellationToken ct) =>
        io.WriteJsonAsync(new JsonObject { ["t"] = "denied", ["reason"] = reason, ["message"] = message }, ct);

    static string? Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }

    internal static bool IsDisconnect(Exception ex) =>
        ex is IOException or ObjectDisposedException or OperationCanceledException or SocketException
            or ProtocolException or AuthenticationException or COMException;

    static void SafeDispose(Stream stream)
    {
        try { stream.Dispose(); }
        catch (Exception) { }
    }
}
