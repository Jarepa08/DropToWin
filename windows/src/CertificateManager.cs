using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DropToWin;

/// <summary>
/// Certificado TLS autofirmado para el Wi-Fi turbo. El teléfono no confía en él por
/// una autoridad: lo fija por huella SHA-256, que recibe por el canal Bluetooth.
/// </summary>
static class CertificateManager
{
    static readonly byte[] Entropy = "DropToWin.cert.v1"u8.ToArray();

    public static X509Certificate2 LoadOrCreate(string dataDir)
    {
        var file = Path.Combine(dataDir, "certificado.bin");
        if (File.Exists(file))
        {
            try
            {
                var pfx = ProtectedData.Unprotect(File.ReadAllBytes(file), Entropy, DataProtectionScope.CurrentUser);
                var loaded = X509CertificateLoader.LoadPkcs12(pfx, null);
                if (loaded.HasPrivateKey && loaded.NotAfter > DateTime.Now.AddDays(30))
                    return loaded;
            }
            catch (Exception ex) when (ex is CryptographicException or IOException)
            {
                Log.Error("Certificado", ex);
            }
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=DropToWin", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));

        // SChannel no puede usar la clave efímera de CreateSelfSigned: se exporta y se vuelve a cargar.
        var pfxBytes = created.Export(X509ContentType.Pkcs12);
        File.WriteAllBytes(file, ProtectedData.Protect(pfxBytes, Entropy, DataProtectionScope.CurrentUser));
        return X509CertificateLoader.LoadPkcs12(pfxBytes, null);
    }

    public static string Fingerprint(X509Certificate2 cert) =>
        Convert.ToHexStringLower(SHA256.HashData(cert.RawData));
}
