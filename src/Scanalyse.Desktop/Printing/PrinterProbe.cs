using System.IO;
using System.Net.Sockets;
using System.Text;
using Scanalyse.Core.Printing;

namespace Scanalyse.Desktop.Printing;

/// <summary>Résultat d'une recherche d'imprimante.</summary>
/// <param name="Profile">Ce qui a été appris (marque, modèle, langages, réglages), ou <c>null</c>.</param>
/// <param name="Reachable">L'imprimante a répondu sur le réseau.</param>
/// <param name="Target">Adresse interrogée, ou <c>null</c> (imprimante USB).</param>
internal sealed record ProbeResult(PrinterProfile? Profile, bool Reachable, string? Target);

/// <summary>
/// Identifie l'imprimante choisie, toutes marques : SNMP (description, modèle, langages de la MIB imprimante), nom du
/// pilote Windows, puis, pour une Zebra, lecture de tous ses réglages (« allcv ») et, pour une TSC, de son modèle (~!T).
/// Rien n'est imprimé : seules des commandes de lecture propres à la marque reconnue sont envoyées.
/// </summary>
internal static class PrinterProbe
{
    /// <summary>
    /// Recherche l'imprimante. <paramref name="manual"/> (bouton « Lire l'imprimante ») : lecture des réglages Zebra tentée
    /// même si la marque n'a pas été reconnue avant.
    /// </summary>
    public static async Task<ProbeResult> ProbeAsync(string? target, string? queue, bool manual, CancellationToken cancel)
    {
        var names = string.Join(' ', new[] { queue, queue is null ? null : PrinterTransport.QueueDriver(queue) }.Where(n => !string.IsNullOrWhiteSpace(n)));
        var brand = PrinterConfig.DetectBrand(names);
        if (string.IsNullOrWhiteSpace(target))
        {
            return new ProbeResult(brand == PrinterBrand.Unknown ? null : new PrinterProfile(brand, ModelFrom(names, brand), null, [], Empty, "pilote Windows"), false, null);
        }

        var (host, _) = PrinterTransport.ParseTarget(target);

        // 1. SNMP : toutes les marques, en parallèle, sans rien imprimer.
        var oids = new List<string> { Snmp.SysDescr, Snmp.DeviceDescr };
        oids.AddRange(Enumerable.Range(1, 6).Select(i => $"{Snmp.InterpreterDescrPrefix}.{i}"));
        var answers = await Task.WhenAll(oids.Select((oid, i) => SnmpGetAsync(host, oid, 100 + i, cancel)));
        cancel.ThrowIfCancellationRequested();
        var sysDescr = answers[0];
        var deviceDescr = answers[1];
        var interpreters = answers.Skip(2).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!).ToList();
        var snmpReached = answers.Any(a => a is not null);
        var described = PrinterConfig.DetectBrand($"{deviceDescr} {sysDescr}");
        if (described != PrinterBrand.Unknown)
        {
            brand = described;
        }

        // 2. Zebra : tous les réglages, qui font référence pour l'analyse.
        if (brand == PrinterBrand.Zebra || (manual && brand == PrinterBrand.Unknown))
        {
            try
            {
                var reply = await PrinterTransport.QueryTcpAsync(target, PrinterConfig.AllcvRequest, cancel, maxBytes: 2 * 1024 * 1024, firstWaitMs: manual ? 6000 : 4000, quietMs: 1500);
                var values = PrinterConfig.ParseAllcv(Encoding.UTF8.GetString(reply));
                if (values.Count > 0)
                {
                    var zebra = PrinterConfig.ZebraProfile(values, "réseau : réglages lus sur l'imprimante");
                    return new ProbeResult(zebra with { Model = zebra.Model ?? ModelFrom($"{deviceDescr} {names}", PrinterBrand.Zebra) }, true, target);
                }
            }
            catch (IOException) when (snmpReached || brand == PrinterBrand.Zebra)
            {
                // Port d'impression fermé ou occupé : on garde ce que SNMP a appris.
            }
        }

        var reachable = snmpReached || await CanConnectAsync(target, cancel);
        var model = ModelFrom(deviceDescr, brand) ?? ModelFrom(sysDescr, brand);

        // 3. TSC : le modèle par sa propre commande de lecture, si SNMP ne l'a pas donné.
        if (brand == PrinterBrand.Tsc && model is null && reachable)
        {
            try
            {
                var reply = await PrinterTransport.QueryTcpAsync(target, Encoding.ASCII.GetBytes("~!T\r\n"), cancel, maxBytes: 256, firstWaitMs: 2000, quietMs: 300);
                var text = Encoding.ASCII.GetString(reply).Trim('\0', '\r', '\n', ' ');
                model = text.Length is > 0 and < 60 ? text : null;
            }
            catch (IOException)
            {
            }
        }

        if (brand == PrinterBrand.Unknown && !reachable)
        {
            return new ProbeResult(null, false, target);
        }

        var languages = PrinterConfig.LanguagesFrom(interpreters);
        var source = snmpReached ? "réseau (SNMP)" : reachable ? "réseau" : "pilote Windows";
        var profile = new PrinterProfile(brand, model ?? ModelFrom(names, brand), FirmwareFrom(sysDescr), languages, Empty, source);
        return new ProbeResult(brand == PrinterBrand.Unknown && model is null ? null : profile, reachable, target);
    }

    private static readonly IReadOnlyDictionary<string, PrinterValue> Empty = new Dictionary<string, PrinterValue>();

    /// <summary>Modèle tiré d'une description (« Zebra Technologies ZTC ZT411-203dpi ZPL » → « ZT411-203dpi ZPL »).</summary>
    private static string? ModelFrom(string? text, PrinterBrand brand)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim();
        foreach (var word in new[] { "Zebra Technologies", "ZTC", "ZDesigner", "Honeywell", "Intermec", "Datamax-O'Neil", "Datamax", "SATO", "TSC", "Printer", "Imprimante" })
        {
            t = t.Replace(word, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        t = string.Join(' ', t.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)).Trim('-', ' ');
        return t.Length is 0 or > 60 || (brand == PrinterBrand.Unknown && t.Length > 40) ? null : t;
    }

    /// <summary>Firmware cité dans la description SNMP (« … V92.21.39Z », « FW 1.2.3 »), si présent.</summary>
    private static string? FirmwareFrom(string? sysDescr)
    {
        if (sysDescr is null)
        {
            return null;
        }

        var m = System.Text.RegularExpressions.Regex.Match(sysDescr, @"\b(V\d+(?:\.\d+)+\w*|(?:FW|Firmware|Version)[\s:]*[\w.\-]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Value : null;
    }

    /// <summary>Lecture SNMP d'un OID (communauté « public », 1,2 s) ; <c>null</c> sans réponse.</summary>
    private static async Task<string?> SnmpGetAsync(string host, string oid, int requestId, CancellationToken cancel)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Connect(host, 161);
            var request = Snmp.BuildGet(oid, requestId);
            await udp.SendAsync(request, cancel);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(1200);
            var reply = await udp.ReceiveAsync(timeout.Token);
            var parsed = Snmp.ParseResponse(reply.Buffer);
            return parsed is { Error: 0 } p && p.RequestId == requestId && p.Values.Count > 0 ? p.Values[0].Value : null;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>Le port d'impression répond (connexion ouverte puis refermée, rien n'est envoyé).</summary>
    private static async Task<bool> CanConnectAsync(string target, CancellationToken cancel)
    {
        var (host, port) = PrinterTransport.ParseTarget(target);
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(1500);
            await client.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
