using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace DropToWin.Server;

/// <summary>
/// Wi-Fi turbo: el mismo protocolo sobre TCP + TLS. Solo acepta sesiones con una llave
/// entregada antes por Bluetooth.
/// </summary>
sealed class WifiServer(SessionHandler sessions, X509Certificate2 certificate, bool loopbackOnly, CancellationToken appToken) : IDisposable
{
    public const int DefaultPort = 47811;

    TcpListener? listener;

    public int Port => DefaultPort;
    public bool IsListening { get; private set; }
    public string? Error { get; private set; }

    public void Start()
    {
        try
        {
            listener = new TcpListener(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, Port);
            listener.ExclusiveAddressUse = true;
            listener.Start();
            IsListening = true;
            _ = AcceptLoopAsync(listener);
        }
        catch (SocketException ex)
        {
            Error = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"el puerto {Port} lo usa otra aplicación"
                : ex.Message;
            Log.Error("Wi-Fi", ex);
        }
    }

    /// <summary>Direcciones que se ofrecen al teléfono (IPv4/prefijo).</summary>
    public IReadOnlyList<string> OfferAddresses() => loopbackOnly
        ? ["127.0.0.1/8"]
        : LocalNetwork.GetAddresses().Select(a => $"{a.Address}/{a.PrefixLength}").ToList();

    async Task AcceptLoopAsync(TcpListener l)
    {
        while (!appToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await l.AcceptTcpClientAsync(appToken);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex)
            {
                Log.Error("Wi-Fi", ex);
                continue;
            }
            _ = HandleAsync(client);
        }
    }

    async Task HandleAsync(TcpClient client)
    {
        try
        {
            client.NoDelay = true;
            var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "Wi-Fi";
            var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(appToken))
            {
                handshake.CancelAfter(TimeSpan.FromSeconds(10));
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ClientCertificateRequired = false,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                }, handshake.Token);
            }
            await sessions.RunAsync(ssl, new PeerInfo("wifi", remote, null, null), appToken);
        }
        catch (Exception ex) when (SessionHandler.IsDisconnect(ex))
        {
            Log.Info("Wi-Fi", "Conexión descartada: " + ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("Wi-Fi", ex);
        }
        finally
        {
            client.Dispose();
        }
    }

    public void Dispose()
    {
        IsListening = false;
        listener?.Stop();
    }
}

/// <summary>
/// Solo para desarrollo (--test-transport): TCP sin cifrar en 127.0.0.1:47819 que el PC trata
/// como si fuera una conexión Bluetooth. Permite probar el protocolo completo sin teléfono.
/// </summary>
sealed class TestTransportServer(SessionHandler sessions, CancellationToken appToken) : IDisposable
{
    public const int Port = 47819;
    readonly TcpListener listener = new(IPAddress.Loopback, Port);

    public void Start()
    {
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (!appToken.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(appToken); }
                catch (Exception) { break; }
                client.NoDelay = true;
                _ = sessions.RunAsync(client.GetStream(),
                    new PeerInfo("bluetooth", "Transporte de prueba", "00:00:00:00:00:00", true), appToken)
                    .ContinueWith(_ => client.Dispose(), TaskScheduler.Default);
            }
        });
        Log.Info("Prueba", $"Transporte de prueba en 127.0.0.1:{Port}");
    }

    public void Dispose() => listener.Stop();
}
