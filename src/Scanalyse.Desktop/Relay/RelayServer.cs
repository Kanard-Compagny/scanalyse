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
internal sealed partial class RelayServer : IAsyncDisposable
{
    public const int HttpPort = 8765;
    public const int RawPort = 8766;

    private TcpListener? _http;
    private TcpListener? _raw;
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
        _ = AcceptLoopAsync(_http, HandleHttpAsync, _cts.Token);
        _ = AcceptLoopAsync(_raw, HandleRawAsync, _cts.Token);
        return null;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _http?.Stop();
        _raw?.Stop();
        _http = null;
        _raw = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>Adresses IPv4 du PC sur le réseau local (pour l'adresse Wi-Fi du relais).</summary>
    public static IReadOnlyList<string> LanAddresses()
    {
        var list = new List<string>();
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
                var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var a in props.UnicastAddresses)
                {
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)
                        && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    {
                        if (hasGateway)
                        {
                            list.Insert(0, a.Address.ToString());
                        }
                        else
                        {
                            list.Add(a.Address.ToString());
                        }
                    }
                }
            }
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
        }

        return list.Distinct().ToList();
    }

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

    private async Task HandleHttpAsync(TcpClient client, CancellationToken token)
    {
        client.ReceiveTimeout = 10000;
        var stream = client.GetStream();
        var (method, path, body) = await ReadRequestAsync(stream, token);
        if (method is null)
        {
            return;
        }

        if (method == "GET" && path is "/" or "/relais")
        {
            await RespondAsync(stream, 200, "text/html; charset=utf-8", RelayPage.Value);
            Log?.Invoke(LogKind.Info, $"Page de relais ouverte sur un terminal ({Origin(client)}).");
        }
        else if (method == "GET" && path == "/ping")
        {
            await RespondAsync(stream, 200, "text/plain; charset=utf-8", "ok"u8.ToArray());
        }
        else if (method == "POST" && path == "/scan")
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                var ua = root.TryGetProperty("userAgent", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                var model = ModelPattern().Match(ua) is { Success: true } m && m.Groups[1].Value.Trim().Length > 2 ? m.Groups[1].Value.Trim() : "Terminal Android";
                var detail = DescribeKeys(root);
                Scanned?.Invoke(new ScanEvent(DateTime.Now, $"{model} · relais {Origin(client)}", Encoding.UTF8.GetBytes(text), text, null, detail));
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

    private static async Task<(string? Method, string Path, byte[] Body)> ReadRequestAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        var used = 0;
        int headerEnd;
        while ((headerEnd = IndexOf(buffer, used, "\r\n\r\n"u8)) < 0)
        {
            if (used == buffer.Length)
            {
                return (null, string.Empty, []);
            }

            var n = await stream.ReadAsync(buffer.AsMemory(used), token);
            if (n == 0)
            {
                return (null, string.Empty, []);
            }

            used += n;
        }

        var head = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var parts = head[0].Split(' ');
        if (parts.Length < 2)
        {
            return (null, string.Empty, []);
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
        return (parts[0].ToUpperInvariant(), path, body);
    }

    private static int IndexOf(byte[] data, int count, ReadOnlySpan<byte> pattern) => data.AsSpan(0, count).IndexOf(pattern);

    private static async Task RespondAsync(Stream stream, int status, string type, byte[] body)
    {
        var reason = status switch { 200 => "OK", 400 => "Bad Request", _ => "Not Found" };
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(body);
        await stream.FlushAsync();
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
