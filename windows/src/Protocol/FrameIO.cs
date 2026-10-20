using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DropToWin.Protocol;

sealed class ProtocolException(string message) : Exception(message);

readonly record struct Frame(byte Kind, ReadOnlyMemory<byte> Payload);

/// <summary>
/// Tramas del protocolo: [u32 big-endian longitud][1 byte tipo][carga].
/// Tipo 'J' = objeto JSON UTF-8, tipo 'D' = bytes del archivo en curso.
/// </summary>
sealed class FrameIO(Stream stream)
{
    public const int MaxPayload = 1 << 20;
    public const byte KindJson = (byte)'J';
    public const byte KindData = (byte)'D';

    readonly byte[] header = new byte[5];
    readonly byte[] writeHeader = new byte[5];
    byte[] buffer = new byte[64 * 1024];

    /// <summary>Lee la siguiente trama. La carga devuelta es válida solo hasta la próxima lectura.</summary>
    public async Task<Frame> ReadAsync(CancellationToken ct)
    {
        await ReadExactAsync(header, header.Length, ct);
        uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
        byte kind = header[4];
        if (length > MaxPayload)
            throw new ProtocolException($"Trama demasiado grande ({length} bytes)");
        if (kind != KindJson && kind != KindData)
            throw new ProtocolException($"Tipo de trama desconocido ({kind})");
        if (buffer.Length < length)
            buffer = new byte[Math.Max(length, buffer.Length * 2)];
        await ReadExactAsync(buffer, (int)length, ct);
        return new Frame(kind, buffer.AsMemory(0, (int)length));
    }

    public async Task<JsonObject> ReadJsonAsync(CancellationToken ct)
    {
        var frame = await ReadAsync(ct);
        if (frame.Kind != KindJson)
            throw new ProtocolException("Se esperaba un mensaje JSON");
        try
        {
            return JsonNode.Parse(frame.Payload.Span) as JsonObject
                ?? throw new ProtocolException("El mensaje no es un objeto JSON");
        }
        catch (JsonException ex)
        {
            throw new ProtocolException("JSON inválido: " + ex.Message);
        }
    }

    public async Task WriteJsonAsync(JsonObject message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        BinaryPrimitives.WriteUInt32BigEndian(writeHeader, (uint)bytes.Length);
        writeHeader[4] = KindJson;
        await stream.WriteAsync(writeHeader, ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    async Task ReadExactAsync(byte[] target, int count, CancellationToken ct)
    {
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(target.AsMemory(read, count - read), ct);
            if (n == 0) throw new EndOfStreamException("La conexión se cerró");
            read += n;
        }
    }
}

static class JsonExtensions
{
    public static string? Str(this JsonObject o, string key) =>
        o.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    public static long? Long(this JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var node) || node is not JsonValue v) return null;
        if (v.TryGetValue(out long l)) return l;
        if (v.TryGetValue(out double d) && d >= long.MinValue && d <= long.MaxValue) return (long)d;
        return null;
    }
}
