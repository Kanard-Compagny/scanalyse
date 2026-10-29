using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Scanalyse.Desktop.Relay;

/// <summary>
/// HTTPS du relais. Beaucoup de terminaux (navigateur géré par MDM, Chrome en « HTTPS uniquement ») bloquent les pages
/// en http://, et la caméra n'est utilisable que sur une page sécurisée.
/// <list type="bullet">
/// <item>Une <b>autorité locale</b> propre à ce PC, créée une fois et gardée 10 ans : c'est elle qu'on installe sur les
/// terminaux (ou qu'un MDM déploie) pour ne plus jamais voir d'avertissement de sécurité.</item>
/// <item>Le <b>certificat du relais</b>, signé par cette autorité, couvre localhost, 127.0.0.1, le nom du PC et ses
/// adresses réseau du moment ; il est refait automatiquement quand une adresse change (DHCP, autre réseau) ou avant
/// son expiration, sans rien réinstaller sur les terminaux.</item>
/// </list>
/// </summary>
internal static class RelayCertificate
{
    private static readonly object Gate = new();
    private static X509Certificate2? _authority;
    private static X509Certificate2? _site;
    private static SslStreamCertificateContext? _context;
    private static DateTime _checked;

    private static string AuthorityPath => Path.Combine(AppSettings.DataFolder, "relais-autorite.pfx");

    private static string SitePath => Path.Combine(AppSettings.DataFolder, "relais-site.pfx");

    /// <summary>Nom du certificat d'autorité, tel qu'il apparaît dans les réglages du terminal.</summary>
    public static string AuthorityName => $"Scanalyse - relais de {Environment.MachineName}";

    /// <summary>Contexte TLS (certificat du relais + autorité) à jour des adresses réseau du PC.</summary>
    public static SslStreamCertificateContext Context()
    {
        lock (Gate)
        {
            // Les adresses ne sont revérifiées que toutes les 30 s : la poignée de main reste rapide.
            if (_context is not null && DateTime.UtcNow - _checked < TimeSpan.FromSeconds(30))
            {
                return _context;
            }

            _checked = DateTime.UtcNow;
            var authority = Authority();
            if (_site is null || !Covers(_site, authority))
            {
                _site = LoadSite(authority) ?? CreateSite(authority);
                _context = null;
            }

            return _context ??= SslStreamCertificateContext.Create(_site, new X509Certificate2Collection(authority), offline: true);
        }
    }

    /// <summary>Certificat d'autorité au format DER (.crt), à installer sur les terminaux.</summary>
    public static byte[] AuthorityDer()
    {
        lock (Gate)
        {
            return Authority().Export(X509ContentType.Cert);
        }
    }

    /// <summary>Empreinte SHA-256 de l'autorité, pour la reconnaître dans les réglages du terminal.</summary>
    public static string AuthorityFingerprint()
    {
        var hash = SHA256.HashData(AuthorityDer());
        return string.Join(":", hash.Take(6).Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))) + "…";
    }

    private static X509Certificate2 Authority()
    {
        if (_authority is not null)
        {
            return _authority;
        }

        try
        {
            if (File.Exists(AuthorityPath))
            {
                var existing = X509CertificateLoader.LoadPkcs12(File.ReadAllBytes(AuthorityPath), null);
                if (existing.HasPrivateKey && existing.NotAfter > DateTime.Now.AddDays(30))
                {
                    return _authority = existing;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={AuthorityName}, O=Scanalyse", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var created = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(10));
        var pfx = created.Export(X509ContentType.Pkcs12);
        Save(AuthorityPath, pfx);

        // Nouvelle autorité : l'ancien certificat du relais n'est plus valable.
        _site = null;
        _context = null;
        return _authority = X509CertificateLoader.LoadPkcs12(pfx, null);
    }

    private static X509Certificate2? LoadSite(X509Certificate2 authority)
    {
        try
        {
            if (File.Exists(SitePath))
            {
                var site = X509CertificateLoader.LoadPkcs12(File.ReadAllBytes(SitePath), null);
                if (site.HasPrivateKey && Covers(site, authority))
                {
                    return site;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
        }

        return null;
    }

    /// <summary>Certificat signé par l'autorité, encore valable 30 jours, et couvrant toutes les adresses actuelles du PC.</summary>
    private static bool Covers(X509Certificate2 site, X509Certificate2 authority)
    {
        if (site.Issuer != authority.Subject || site.NotAfter < DateTime.Now.AddDays(30))
        {
            return false;
        }

        var san = site.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (san is null)
        {
            return false;
        }

        var known = san.EnumerateIPAddresses().Select(a => a.ToString()).ToHashSet(StringComparer.Ordinal);
        return RelayServer.LanAddresses().All(known.Contains);
    }

    private static X509Certificate2 CreateSite(X509Certificate2 authority)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Scanalyse relais, O=Scanalyse", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
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
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(authority, true, false));

        // 397 jours au plus : la durée acceptée par tous les navigateurs ; renouvelé seul avant l'échéance.
        var notAfter = DateTimeOffset.Now.AddDays(397);
        if (notAfter > authority.NotAfter)
        {
            notAfter = authority.NotAfter;
        }

        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        using var signed = request.Create(authority, DateTimeOffset.Now.AddDays(-1), notAfter, serial);
        using var withKey = signed.CopyWithPrivateKey(rsa);
        var pfx = withKey.Export(X509ContentType.Pkcs12);
        Save(SitePath, pfx);

        // Rechargé depuis le PFX : SslStream sous Windows refuse les clés éphémères.
        return X509CertificateLoader.LoadPkcs12(pfx, null);
    }

    private static void Save(string path, byte[] pfx)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            File.WriteAllBytes(path, pfx);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
