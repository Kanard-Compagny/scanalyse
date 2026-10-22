using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Scanalyse.Desktop.Relay;

/// <summary>
/// Certificat auto-signé du relais en HTTPS : en Wi-Fi, le navigateur du terminal n'ouvre la caméra que sur une page
/// sécurisée. Créé une fois, gardé dans le dossier des données (le terminal n'accepte l'avertissement qu'une fois).
/// </summary>
internal static class RelayCertificate
{
    private static X509Certificate2? _cached;

    private static string FilePath => Path.Combine(AppSettings.DataFolder, "relais.pfx");

    public static X509Certificate2 Get()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            if (File.Exists(FilePath))
            {
                var existing = X509CertificateLoader.LoadPkcs12(File.ReadAllBytes(FilePath), null);
                if (existing.NotAfter > DateTime.Now.AddDays(30))
                {
                    return _cached = existing;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Scanalyse relais", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddDnsName(Environment.MachineName);
        names.AddIpAddress(IPAddress.Loopback);
        foreach (var address in RelayServer.LanAddresses())
        {
            if (IPAddress.TryParse(address, out var ip))
            {
                names.AddIpAddress(ip);
            }
        }

        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var created = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
        var pfx = created.Export(X509ContentType.Pkcs12);
        try
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            File.WriteAllBytes(FilePath, pfx);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        // Rechargé depuis le PFX : SslStream sous Windows refuse les clés éphémères.
        return _cached = X509CertificateLoader.LoadPkcs12(pfx, null);
    }
}
