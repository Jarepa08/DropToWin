using System.Security.Cryptography.X509Certificates;
using System.Text;
using DropToWin.Server;
using DropToWin.Transfers;

namespace DropToWin;

/// <summary>Reúne la configuración, los servidores y el almacén de transferencias.</summary>
sealed class AppHost : IAsyncDisposable
{
    readonly CancellationTokenSource stopping = new();
    readonly TestTransportServer? testTransport;

    public AppOptions Options { get; }
    public string DataDir { get; }
    public AppConfig Config { get; }
    public X509Certificate2 Certificate { get; }
    public string Fingerprint { get; }
    public HistoryStore History { get; }
    public TransferStore Transfers { get; }
    public SessionTokens Tokens { get; } = new();
    public SessionHandler Sessions { get; }
    public BluetoothServer Bluetooth { get; }
    public WifiServer Wifi { get; }
    public IphoneServer Iphone { get; }

    /// <summary>Lo asigna la interfaz: muestra el diálogo de aprobación y devuelve la respuesta.</summary>
    public Func<ApprovalRequest, CancellationToken, Task<bool>>? ApprovalHandler { get; set; }

    /// <summary>Texto recibido (se dispara desde un hilo de fondo).</summary>
    public event Action<HistoryItem>? TextReceived;

    public AppHost(AppOptions options)
    {
        Options = options;
        DataDir = options.DataDir ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropToWin");
        Directory.CreateDirectory(DataDir);
        Log.Init(DataDir);
        Log.Info("Inicio", $"DropToWin {Application.ProductVersion} · datos en {DataDir}");

        var defaultFolder = options.DataDir is not null
            ? Path.Combine(DataDir, "Recibidos")
            : Path.Combine(KnownFolders.Downloads, "DropToWin");
        Config = AppConfig.Load(Path.Combine(DataDir, "config.json"), defaultFolder);
        Directory.CreateDirectory(Config.ReceiveFolder);

        Certificate = CertificateManager.LoadOrCreate(DataDir);
        Fingerprint = CertificateManager.Fingerprint(Certificate);

        History = new HistoryStore(Path.Combine(DataDir, "historial.json"));
        Transfers = new TransferStore(Config);
        Transfers.FileCompleted += (batch, file) => History.Add(new HistoryItem
        {
            Kind = "file",
            Name = Path.GetFileName(file.SavedPath) ?? file.Name,
            Path = file.SavedPath,
            Size = file.Size,
            Sender = batch.Sender,
            Transport = batch.Transport,
            TimeUtc = DateTime.UtcNow,
        });

        Sessions = new SessionHandler(this);
        Bluetooth = new BluetoothServer(Sessions, stopping.Token);
        Wifi = new WifiServer(Sessions, Certificate, options.Loopback, stopping.Token);
        Iphone = new IphoneServer(this, options.Loopback);
        if (options.TestTransport)
            testTransport = new TestTransportServer(Sessions, stopping.Token);
    }

    public void Start()
    {
        Wifi.Start();
        testTransport?.Start();
        _ = Task.Run(Bluetooth.StartAsync);
        // Se espera (fuera del hilo de la interfaz, para no bloquearlo) para que la ventana
        // ya muestre el estado real del receptor de iPhone.
        Task.Run(Iphone.StartAsync).GetAwaiter().GetResult();
    }

    public async Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, TimeSpan timeout, CancellationToken ct)
    {
        var handler = ApprovalHandler;
        if (handler is null) return ApprovalResult.Rejected;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await handler(request, cts.Token) ? ApprovalResult.Approved : ApprovalResult.Rejected;
        }
        catch (OperationCanceledException)
        {
            return ApprovalResult.TimedOut;
        }
    }

    public void RevokeDevice(string deviceId)
    {
        Config.RemoveDevice(deviceId);
        Tokens.RevokeDevice(deviceId);
    }

    public void OnTextReceived(string sender, string text, string transport)
    {
        var oneLine = string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        var item = new HistoryItem
        {
            Kind = "text",
            Name = oneLine.Length > 80 ? oneLine[..80] + "…" : oneLine,
            Text = text,
            Size = Encoding.UTF8.GetByteCount(text),
            Sender = sender,
            Transport = transport,
            TimeUtc = DateTime.UtcNow,
        };
        History.Add(item);
        TextReceived?.Invoke(item);
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        await Bluetooth.DisposeAsync();
        await Iphone.DisposeAsync();
        Wifi.Dispose();
        testTransport?.Dispose();
        Transfers.Dispose();
        Log.Info("Inicio", "DropToWin cerrado");
    }
}
