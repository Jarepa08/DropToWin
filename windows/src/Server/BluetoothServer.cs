using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Radios;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace DropToWin.Server;

enum BluetoothState { Starting, Active, Off, NoAdapter, Unsupported, Error }

/// <summary>
/// Servicio RFCOMM anunciado por SDP con el UUID de DropToWin. El teléfono (emparejado con
/// el PC desde los ajustes del sistema) se conecta a este servicio.
/// </summary>
sealed class BluetoothServer(SessionHandler sessions, CancellationToken appToken) : IAsyncDisposable
{
    public static readonly Guid ServiceUuid = new("4f9a6c1e-2b7d-4c3a-9e58-d17b2a6f0c93");
    const ushort SdpServiceNameAttributeId = 0x100;
    const byte SdpServiceNameAttributeType = (4 << 3) | 5;   // cadena de texto, longitud en 1 byte

    readonly SemaphoreSlim gate = new(1, 1);
    RfcommServiceProvider? provider;
    StreamSocketListener? listener;
    Radio? radio;

    public BluetoothState State { get; private set; } = BluetoothState.Starting;
    public string? ErrorMessage { get; private set; }

    /// <summary>Se dispara desde un hilo de fondo.</summary>
    public event Action? StateChanged;

    public async Task StartAsync()
    {
        await gate.WaitAsync();
        try
        {
            StopAdvertising();
            if (appToken.IsCancellationRequested) return;

            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is null) { SetState(BluetoothState.NoAdapter); return; }
            if (!adapter.IsClassicSupported) { SetState(BluetoothState.Unsupported); return; }

            if (radio is null)
            {
                radio = await adapter.GetRadioAsync();
                radio.StateChanged += OnRadioStateChanged;
            }
            if (radio.State != RadioState.On) { SetState(BluetoothState.Off); return; }

            provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(ServiceUuid));
            listener = new StreamSocketListener();
            listener.ConnectionReceived += OnConnectionReceived;
            await listener.BindServiceNameAsync(provider.ServiceId.AsString(),
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
            WriteServiceName(provider, "DropToWin");
            // Sin hacer visible el PC: los teléfonos ya emparejados encuentran el servicio por SDP,
            // y para emparejar uno nuevo Windows lo hace visible mientras su página de Bluetooth está abierta.
            provider.StartAdvertising(listener, radioDiscoverable: false);
            ErrorMessage = null;
            SetState(BluetoothState.Active);
            Log.Info("Bluetooth", "Servicio RFCOMM anunciado");
        }
        catch (Exception ex)
        {
            Log.Error("Bluetooth", ex);
            ErrorMessage = ex.Message;
            StopAdvertising();
            SetState(BluetoothState.Error);
        }
        finally
        {
            gate.Release();
        }
    }

    void OnRadioStateChanged(Radio sender, object args)
    {
        bool shouldRun = sender.State == RadioState.On;
        bool running = State == BluetoothState.Active;
        if (shouldRun != running)
            _ = Task.Run(StartAsync);
    }

    async void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        var socket = args.Socket;
        try
        {
            string display = "Teléfono";
            string? address = null;
            bool? paired = null;
            try
            {
                using var device = await BluetoothDevice.FromHostNameAsync(socket.Information.RemoteHostName);
                if (device is not null)
                {
                    display = string.IsNullOrWhiteSpace(device.Name) ? display : device.Name;
                    address = FormatAddress(device.BluetoothAddress);
                    paired = device.DeviceInformation?.Pairing?.IsPaired;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Bluetooth", ex);
                address = socket.Information.RemoteHostName?.RawName?.Trim('(', ')');
            }

            var stream = new DuplexStream(
                socket.InputStream.AsStreamForRead(64 * 1024),
                socket.OutputStream.AsStreamForWrite(0),
                socket);
            await sessions.RunAsync(stream, new PeerInfo("bluetooth", display, address, paired), appToken);
        }
        catch (Exception ex)
        {
            Log.Error("Bluetooth", ex);
            socket.Dispose();
        }
    }

    static void WriteServiceName(RfcommServiceProvider provider, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        var writer = new DataWriter();
        writer.WriteByte(SdpServiceNameAttributeType);
        writer.WriteByte((byte)bytes.Length);
        writer.WriteBytes(bytes);
        provider.SdpRawAttributes.Add(SdpServiceNameAttributeId, writer.DetachBuffer());
    }

    static string FormatAddress(ulong address) =>
        string.Join(":", Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("X2")));

    void StopAdvertising()
    {
        try { provider?.StopAdvertising(); } catch (Exception ex) { Log.Error("Bluetooth", ex); }
        listener?.Dispose();
        provider = null;
        listener = null;
    }

    void SetState(BluetoothState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (radio is not null) radio.StateChanged -= OnRadioStateChanged;
            StopAdvertising();
        }
        finally
        {
            gate.Release();
        }
    }
}

/// <summary>Une el flujo de entrada y el de salida de un socket WinRT en un solo Stream.</summary>
sealed class DuplexStream(Stream input, Stream output, IDisposable? owner) : Stream
{
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => input.ReadAsync(buffer, ct);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => input.ReadAsync(buffer, offset, count, ct);
    public override void Write(byte[] buffer, int offset, int count) => output.Write(buffer, offset, count);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => output.WriteAsync(buffer, ct);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => output.WriteAsync(buffer, offset, count, ct);
    public override void Flush() => output.Flush();
    public override Task FlushAsync(CancellationToken ct) => output.FlushAsync(ct);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { input.Dispose(); } catch (Exception) { }
            try { output.Dispose(); } catch (Exception) { }
            try { owner?.Dispose(); } catch (Exception) { }
        }
        base.Dispose(disposing);
    }
}
