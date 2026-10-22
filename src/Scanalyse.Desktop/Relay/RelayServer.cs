using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scanalyse.Core.Devices;
using Scanalyse.Desktop.Scanners;

namespace Scanalyse.Desktop.Relay;

/// <summary>
/// Relais terminal : un petit serveur web local. Le navigateur du terminal Android ouvre la page de relais,
/// la douchette intégrée y tape la lecture, et la page l'envoie ici avec le détail des touches.
/// Un second port reçoit des octets bruts (sortie IP de DataWedge, ou tout envoi TCP).
/// </summary>
/// <remarks>
/// En USB, ADB redirige les ports du terminal vers le PC (« adb reverse ») : le serveur n'écoute que sur la
/// machine locale. En Wi-Fi, il écoute sur le réseau ; Windows demande alors l'autorisation du pare-feu.
/// </remarks>
/// <summary>Terminal relié au relais (page ouverte et active).</summary>
/// <param name="Key">Identifiant (origine + modèle).</param>
/// <param name="Model">Modèle du terminal.</param>
/// <param name="Origin">« USB » ou « Wi-Fi 192.168… ».</param>
public sealed record RelaySession(string Key, string Model, string Origin);

internal sealed partial class RelayServer : IAsyncDisposable
{
    public const int HttpPort = 8765;
    public const int RawPort = 8766;

    /// <summary>Page de relais en HTTPS (Wi-Fi) : nécessaire pour utiliser la caméra du terminal.</summary>
    public const int HttpsPort = 8767;

    /// <summary>Sans signe de vie de la page pendant ce délai, le terminal est considéré comme déconnecté.</summary>
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(7);

    private readonly object _gate = new();
    private readonly Dictionary<string, (RelaySession Session, DateTime Seen)> _sessions = [];
    private readonly HashSet<string> _blocked = [];
    private Timer? _sweeper;

    /// <summary>
    /// Modèle du terminal relié en USB, connu par ADB : les navigateurs récents masquent le modèle dans leur identifiant.
    /// </summary>
    public string? UsbModelHint { get; set; }

    /// <summary>Terminaux actuellement reliés.</summary>
    public IReadOnlyList<RelaySession> Sessions
    {
        get
        {
            lock (_gate)
            {
                return [.. _sessions.Values.Select(v => v.Session)];
            }
        }
    }

    /// <summary>La liste des terminaux reliés a changé (thread quelconque).</summary>
    public event Action? SessionsChanged;

    private TcpListener? _http;
    private TcpListener? _raw;
    private TcpListener? _https;
    private CancellationTokenSource? _cts;

    /// <summary>Écoute sur le réseau (Wi-Fi) et pas seulement en local.</summary>
    public bool Lan { get; private set; }

    public bool IsRunning => _http is not null;

    /// <summary>Lecture reçue (thread quelconque).</summary>
    public event Action<ScanEvent>? Scanned;

    /// <summary>Message pour le journal (thread quelconque).</summary>
    public event Action<LogKind, string>? Log;

    /// <summary>Démarre (ou redémarre) l'écoute. Renvoie un message d'erreur, ou <c>null</c>.</summary>
    public async Task<string?> StartAsync(bool lan)
    {
        await StopAsync();
        var address = lan ? IPAddress.Any : IPAddress.Loopback;
        try
        {
            _http = new TcpListener(address, HttpPort);
            _http.Start();
            _raw = new TcpListener(address, RawPort);
            _raw.Start();
        }
        catch (SocketException ex)
        {
            await StopAsync();
            return $"Ports {HttpPort}/{RawPort} indisponibles ({ex.SocketErrorCode}) : une autre instance de Scanalyse ou un autre programme les utilise.";
        }

        Lan = lan;
        _cts = new CancellationTokenSource();
        _sweeper = new Timer(_ => Sweep(), null, 1000, 1000);
        _ = AcceptLoopAsync(_http, HandleHttpAsync, _cts.Token);
        _ = AcceptLoopAsync(_raw, HandleRawAsync, _cts.Token);
        if (lan)
        {
            // HTTPS en Wi-Fi : le navigateur du terminal n'autorise la caméra que sur une page sécurisée.
            try
            {
                _https = new TcpListener(address, HttpsPort);
                _https.Start();
                _ = AcceptLoopAsync(_https, HandleHttpsAsync, _cts.Token);
            }
            catch (SocketException ex)
            {
                _https = null;
                Log?.Invoke(LogKind.Warning, $"Port {HttpsPort} indisponible ({ex.SocketErrorCode}) : la caméra des terminaux en Wi-Fi ne sera pas utilisable.");
            }
        }

        return null;
    }

    public Task StopAsync()
    {
        _sweeper?.Dispose();
        _sweeper = null;
        lock (_gate)
        {
            _sessions.Clear();
        }

        SessionsChanged?.Invoke();
        _cts?.Cancel();
        _http?.Stop();
        _raw?.Stop();
        _https?.Stop();
        _http = null;
        _raw = null;
        _https = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>Adresse réseau du PC, avec la carte qui la porte.</summary>
    /// <param name="Address">Adresse IPv4.</param>
    /// <param name="Adapter">Nom de la carte réseau (« Wi-Fi », « Ethernet »…).</param>
    /// <param name="Likely">Carte Wi-Fi ou Ethernet physique avec passerelle : la plus probable pour joindre le terminal.</param>
    public sealed record LanAddress(string Address, string Adapter, bool Likely);

    private static readonly string[] VirtualHints = ["virtual", "hyper-v", "vmware", "virtualbox", "wsl", "vethernet", "tap", "vpn", "tunnel", "bluetooth", "loopback", "docker", "zerotier", "tailscale", "fortinet", "cisco anyconnect", "globalprotect"];

    /// <summary>Adresses du PC, la plus probable en premier (cartes virtuelles et VPN en dernier).</summary>
    public static IReadOnlyList<LanAddress> Networks()
    {
        var list = new List<(LanAddress A, int Rank)>();
        try
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up
                    || nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var props = nic.GetIPProperties();
                var gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                var label = (nic.Name + " " + nic.Description).ToLowerInvariant();
                var isVirtual = VirtualHints.Any(label.Contains);
                var wifi = nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211;
                var rank = (isVirtual ? 100 : 0) + (gateway ? 0 : 10) + (wifi ? 0 : 1);
                foreach (var a in props.UnicastAddresses)
                {
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)
                        && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    {
                        list.Add((new LanAddress(a.Address.ToString(), nic.Name, !isVirtual && gateway), rank));
                    }
                }
            }
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
        }

        return [.. list.OrderBy(x => x.Rank).Select(x => x.A).DistinctBy(a => a.Address)];
    }

    /// <summary>Adresses IPv4 du PC sur le réseau local (pour l'adresse Wi-Fi du relais).</summary>
    public static IReadOnlyList<string> LanAddresses() => [.. Networks().Select(n => n.Address)];


    private static async Task AcceptLoopAsync(TcpListener listener, Func<TcpClient, CancellationToken, Task> handler, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                using (client)
                {
                    try
                    {
                        await handler(client, token);
                    }
                    catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                    {
                    }
                }
            }, token);
        }
    }

    private static string Origin(TcpClient client) =>
        client.Client.RemoteEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address) ? $"Wi-Fi {ep.Address}" : "USB";

    // ------------------------------------------------------------------ HTTP (page de relais)

    private async Task HandleHttpsAsync(TcpClient client, CancellationToken token)
    {
        client.ReceiveTimeout = 10000;
        await using var ssl = new System.Net.Security.SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        try
        {
            await ssl.AuthenticateAsServerAsync(new System.Net.Security.SslServerAuthenticationOptions { ServerCertificate = RelayCertificate.Get() }, token);
        }
        catch (Exception ex) when (ex is System.Security.Authentication.AuthenticationException or System.Security.Cryptography.CryptographicException)
        {
            // Terminal qui refuse le certificat (avertissement pas encore accepté) : rien à faire de plus.
            return;
        }

        await HandleRequestAsync(ssl, client, token);
    }

    private async Task HandleHttpAsync(TcpClient client, CancellationToken token)
    {
        client.ReceiveTimeout = 10000;
        await HandleRequestAsync(client.GetStream(), client, token);
    }

    private async Task HandleRequestAsync(Stream stream, TcpClient client, CancellationToken token)
    {
        var (method, path, body, userAgent) = await ReadRequestAsync(stream, token);
        if (method is null)
        {
            return;
        }

        var origin = Origin(client);
        var model = ModelOf(userAgent, origin);
        if (method == "GET" && path is "/" or "/relais")
        {
            // Ouvrir la page (ou « Reprendre la liaison » sur le terminal) relance la liaison.
            Unblock(model, origin);
            await RespondAsync(stream, 200, "text/html; charset=utf-8", RelayPage.Value);
            Log?.Invoke(LogKind.Info, $"Page de relais ouverte sur {model} ({origin}).");
        }
        else if (method == "GET" && path is "/zxing.js" or "/zxing.wasm")
        {
            // Décodeur pour la caméra du terminal (servi par le PC : rien à télécharger sur Internet).
            var js = path == "/zxing.js";
            var file = js ? ZxingJs.Value : ZxingWasm.Value;
            await RespondAsync(stream, file.Length > 0 ? 200 : 404, js ? "text/javascript; charset=utf-8" : "application/wasm", file);
        }
        else if (method == "GET" && path == "/ping")
        {
            // 410 : liaison arrêtée depuis le PC, la page passe en « liaison arrêtée ».
            if (IsBlocked(model, origin))
            {
                await RespondAsync(stream, 410, "text/plain; charset=utf-8", "stop"u8.ToArray());
                return;
            }

            Touch(model, origin);
            await RespondAsync(stream, 200, "text/plain; charset=utf-8", "ok"u8.ToArray());
        }
        else if (method == "POST" && path == "/bye")
        {
            Forget(model, origin);
            Log?.Invoke(LogKind.Info, $"Liaison arrêtée depuis {model} ({origin}).");
            await RespondAsync(stream, 200, "text/plain; charset=utf-8", "ok"u8.ToArray());
        }
        else if (method == "POST" && path == "/scan" && IsBlocked(model, origin))
        {
            await RespondAsync(stream, 410, "text/plain; charset=utf-8", "stop"u8.ToArray());
        }
        else if (method == "POST" && path == "/scan")
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                Touch(model, origin);
                var camera = root.TryGetProperty("camera", out var cam) && cam.ValueKind == JsonValueKind.True;
                var format = root.TryGetProperty("format", out var f) ? f.GetString() ?? string.Empty : string.Empty;
                var detail = camera ? $"caméra du terminal{(format.Length > 0 ? " · " + format : string.Empty)}" : DescribeKeys(root);
                if (text.Length > 0)
                {
                    Scanned?.Invoke(new ScanEvent(DateTime.Now, $"{model} · {(camera ? "caméra" : "relais")} {origin}", Encoding.UTF8.GetBytes(text), text, null, detail));
                }

                await RespondAsync(stream, 200, "text/plain; charset=utf-8", "ok"u8.ToArray());
            }
            catch (JsonException)
            {
                await RespondAsync(stream, 400, "text/plain; charset=utf-8", "json"u8.ToArray());
            }
        }
        else
        {
            await RespondAsync(stream, 404, "text/plain; charset=utf-8", "introuvable"u8.ToArray());
        }
    }

    /// <summary>Résumé des touches : leur nombre, et celles que le navigateur n'a pas su traduire (GS perdu…).</summary>
    private static string DescribeKeys(JsonElement root)
    {
        if (!root.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var total = 0;
        var unknown = new List<string>();
        foreach (var k in keys.EnumerateArray())
        {
            total++;
            var key = k.TryGetProperty("key", out var kk) ? kk.GetString() ?? string.Empty : string.Empty;
            var code = k.TryGetProperty("keyCode", out var kc) && kc.TryGetInt32(out var n) ? n : -1;
            if (key is "Unidentified" or "" || code is 0 or 229)
            {
                unknown.Add(code > 0 ? $"code {code}" : "?");
            }
        }

        return unknown.Count == 0
            ? $"{total} touches"
            : $"{total} touches dont {unknown.Count} non reconnue{(unknown.Count > 1 ? "s" : string.Empty)} par le navigateur ({string.Join(", ", unknown.Distinct().Take(4))})";
    }

    /// <summary>Modèle du terminal : identifiant du navigateur, sinon modèle connu par ADB pour l'USB.</summary>
    private string ModelOf(string userAgent, string origin)
    {
        var fromAgent = ModelPattern().Match(userAgent) is { Success: true } m ? m.Groups[1].Value.Trim() : string.Empty;
        if (fromAgent.Length > 2)
        {
            return fromAgent;
        }

        return origin == "USB" && !string.IsNullOrEmpty(UsbModelHint) ? UsbModelHint : "Terminal Android";
    }

    /// <summary>La page a donné signe de vie.</summary>
    private void Touch(string model, string origin)
    {
        var key = $"{origin}|{model}";
        bool added;
        lock (_gate)
        {
            added = !_sessions.ContainsKey(key);
            _sessions[key] = (new RelaySession(key, model, origin), DateTime.UtcNow);
        }

        if (added)
        {
            SessionsChanged?.Invoke();
        }
    }

    /// <summary>Arrêt demandé depuis le PC : la page du terminal est refusée jusqu'à ce qu'elle soit rouverte.</summary>
    public void Block(string model, string origin)
    {
        lock (_gate)
        {
            _blocked.Add($"{origin}|{model}");
        }

        Forget(model, origin);
    }

    public void Unblock(string model, string origin)
    {
        lock (_gate)
        {
            _blocked.Remove($"{origin}|{model}");
        }
    }

    private bool IsBlocked(string model, string origin)
    {
        lock (_gate)
        {
            return _blocked.Contains($"{origin}|{model}");
        }
    }

    /// <summary>Oublie un terminal (« Arrêter la liaison » sur le terminal ou sur le PC).</summary>
    public void Forget(string model, string origin) => ForgetKey($"{origin}|{model}");

    public void ForgetKey(string key)
    {
        bool removed;
        lock (_gate)
        {
            removed = _sessions.Remove(key);
        }

        if (removed)
        {
            SessionsChanged?.Invoke();
        }
    }

    private void Sweep()
    {
        bool removed;
        lock (_gate)
        {
            var old = _sessions.Where(s => DateTime.UtcNow - s.Value.Seen > SessionTimeout).Select(s => s.Key).ToList();
            old.ForEach(k => _sessions.Remove(k));
            removed = old.Count > 0;
        }

        if (removed)
        {
            SessionsChanged?.Invoke();
        }
    }

    private static async Task<(string? Method, string Path, byte[] Body, string UserAgent)> ReadRequestAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        var used = 0;
        int headerEnd;
        while ((headerEnd = IndexOf(buffer, used, "\r\n\r\n"u8)) < 0)
        {
            if (used == buffer.Length)
            {
                return (null, string.Empty, [], string.Empty);
            }

            var n = await stream.ReadAsync(buffer.AsMemory(used), token);
            if (n == 0)
            {
                return (null, string.Empty, [], string.Empty);
            }

            used += n;
        }

        var head = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var parts = head[0].Split(' ');
        if (parts.Length < 2)
        {
            return (null, string.Empty, [], string.Empty);
        }

        var length = head.Skip(1).Select(h => h.Split(':', 2)).Where(h => h.Length == 2 && h[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(h => int.TryParse(h[1].Trim(), out var l) ? l : 0).FirstOrDefault();
        length = Math.Clamp(length, 0, 1024 * 1024);
        var body = new byte[length];
        var have = Math.Min(length, used - headerEnd - 4);
        Array.Copy(buffer, headerEnd + 4, body, 0, have);
        while (have < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(have), token);
            if (n == 0)
            {
                break;
            }

            have += n;
        }

        var path = parts[1].Split('?')[0];
        var agent = head.Skip(1).Select(h => h.Split(':', 2)).Where(h => h.Length == 2 && h[0].Trim().Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
            .Select(h => h[1].Trim()).FirstOrDefault() ?? string.Empty;
        return (parts[0].ToUpperInvariant(), path, body, agent);
    }

    private static int IndexOf(byte[] data, int count, ReadOnlySpan<byte> pattern) => data.AsSpan(0, count).IndexOf(pattern);

    private static async Task RespondAsync(Stream stream, int status, string type, byte[] body)
    {
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 410 => "Gone", _ => "Not Found" };
        // Le décodeur se met en cache sur le terminal ; le reste jamais.
        var cache = type.StartsWith("application/wasm", StringComparison.Ordinal) || type.StartsWith("text/javascript", StringComparison.Ordinal) ? "max-age=86400" : "no-store";
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: {cache}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static readonly Lazy<byte[]> ZxingJs = new(() => Resource("Scanalyse.Relay.zxing.js"));

    private static readonly Lazy<byte[]> ZxingWasm = new(() => Resource("Scanalyse.Relay.zxing.wasm"));

    private static byte[] Resource(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (s is null)
        {
            return [];
        }

        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static readonly Lazy<byte[]> RelayPage = new(() =>
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Scanalyse.Relay.relais.html");
        if (s is null)
        {
            return "Page de relais absente."u8.ToArray();
        }

        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    });

    // ------------------------------------------------------------------ TCP brut (sortie IP de DataWedge…)

    private async Task HandleRawAsync(TcpClient client, CancellationToken token)
    {
        var origin = Origin(client);
        var frames = new FrameAssembler(TimeSpan.FromMilliseconds(80));
        var stream = client.GetStream();
        var buffer = new byte[16 * 1024];
        while (!token.IsCancellationRequested)
        {
            var read = stream.ReadAsync(buffer, token).AsTask();
            // Pendant le silence, on livre la lecture en attente dès que l'écart de fin est atteint.
            while (await Task.WhenAny(read, Task.Delay(100, token)) != read)
            {
                Emit(frames.Poll(DateTimeOffset.UtcNow), origin);
            }

            var n = await read;
            if (n == 0)
            {
                Emit(frames.Flush(), origin);
                return;
            }

            Emit(frames.Append(buffer.AsSpan(0, n), DateTimeOffset.UtcNow), origin);
        }
    }

    private void Emit(byte[]? bytes, string origin)
    {
        if (bytes is { Length: > 0 })
        {
            Scanned?.Invoke(new ScanEvent(DateTime.Now, $"Terminal · sortie IP {origin}", bytes, RawData.Decode(bytes), null));
        }
    }

    [GeneratedRegex(@"Android [^;)]*;\s*([^;)]+?)(?:\s+Build/|\s*[;)])", RegexOptions.CultureInvariant)]
    private static partial Regex ModelPattern();
}
