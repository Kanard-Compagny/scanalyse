using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Printing;
using System.Runtime.InteropServices;

namespace Scanalyse.Desktop.Printing;

/// <summary>
/// Envoi d'un programme d'étiquette brut à une imprimante : directement sur le réseau (port 9100, « RAW »/JetDirect)
/// ou via une file d'impression Windows en mode RAW (le pilote ne retouche pas les données).
/// </summary>
internal static class PrinterTransport
{
    /// <summary>Port réseau standard des imprimantes d'étiquettes (Zebra, Honeywell, TSC, SATO…).</summary>
    public const int RawPort = 9100;

    /// <summary>Envoie les octets à « adresse » ou « adresse:port ».</summary>
    public static async Task SendTcpAsync(string target, byte[] data, CancellationToken cancel = default)
    {
        var (host, port) = ParseTarget(target);
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await client.ConnectAsync(host, port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync(data, timeout.Token);
            await stream.FlushAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new IOException($"Pas de réponse de {host}:{port} en 5 secondes : vérifie l'adresse IP, que l'imprimante est allumée et sur le même réseau.");
        }
        catch (SocketException ex)
        {
            throw new IOException($"Connexion à {host}:{port} impossible ({ex.SocketErrorCode}) : vérifie l'adresse IP et que le port {port} est ouvert sur l'imprimante.", ex);
        }
    }

    /// <summary>
    /// Envoie une commande et lit la réponse de l'imprimante (état…) : on lit jusqu'à un court silence après les premiers octets.
    /// </summary>
    public static async Task<byte[]> QueryTcpAsync(string target, byte[] data, CancellationToken cancel = default)
    {
        var (host, port) = ParseTarget(target);
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await client.ConnectAsync(host, port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync(data, timeout.Token);
            await stream.FlushAsync(timeout.Token);
            var buffer = new byte[4096];
            using var reply = new MemoryStream();
            var wait = TimeSpan.FromSeconds(3);
            while (reply.Length < 65536)
            {
                using var quiet = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                quiet.CancelAfter(wait);
                int n;
                try
                {
                    n = await stream.ReadAsync(buffer, quiet.Token);
                }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                {
                    break;
                }

                if (n <= 0)
                {
                    break;
                }

                reply.Write(buffer, 0, n);
                wait = TimeSpan.FromMilliseconds(600);
            }

            return reply.ToArray();
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new IOException($"Pas de réponse de {host}:{port} en 5 secondes : vérifie l'adresse IP, que l'imprimante est allumée et sur le même réseau.");
        }
        catch (SocketException ex)
        {
            throw new IOException($"Connexion à {host}:{port} impossible ({ex.SocketErrorCode}) : vérifie l'adresse IP et que le port {port} est ouvert sur l'imprimante.", ex);
        }
    }

    /// <summary>Adresse IP de l'imprimante derrière une file Windows (port TCP/IP standard), ou <c>null</c> (USB, partage…).</summary>
    public static string? QueueAddress(string queue)
    {
        try
        {
            using var server = new LocalPrintServer();
            using var q = queue.StartsWith(@"\\", StringComparison.Ordinal) ? new PrintQueue(new PrintServer(queue[..queue.IndexOf('\\', 2)]), queue[(queue.IndexOf('\\', 2) + 1)..]) : server.GetPrintQueue(queue);
            var port = q.QueuePort?.Name ?? string.Empty;
            var m = System.Text.RegularExpressions.Regex.Match(port, @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (Exception ex) when (ex is PrintQueueException or PrintServerException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>« 192.168.1.50 », « 192.168.1.50:6101 », « imprimante.local ».</summary>
    public static (string Host, int Port) ParseTarget(string target)
    {
        var t = target.Trim();
        var colon = t.LastIndexOf(':');
        if (colon > 0 && t.IndexOf(':') == colon && int.TryParse(t[(colon + 1)..], out var p) && p is > 0 and < 65536)
        {
            return (t[..colon], p);
        }

        return (t, RawPort);
    }

    /// <summary>Files d'impression installées sur ce PC (locales et partagées).</summary>
    public static IReadOnlyList<string> Queues()
    {
        try
        {
            using var server = new LocalPrintServer();
            return [.. server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections])
                .Select(q => q.FullName)
                .Order(StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception ex) when (ex is PrintQueueException or Win32Exception or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>Envoie les octets à une file Windows, en type de données RAW (sans passer par le rendu du pilote).</summary>
    public static void SendToQueue(string queue, byte[] data, string documentName)
    {
        if (!OpenPrinter(queue, out var handle, IntPtr.Zero))
        {
            throw new IOException($"Impossible d'ouvrir l'imprimante « {queue} » : {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        try
        {
            var doc = new DocInfo { Name = documentName, DataType = "RAW" };
            if (StartDocPrinter(handle, 1, doc) == 0)
            {
                throw new IOException($"L'impression a été refusée par « {queue} » : {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }

            try
            {
                StartPagePrinter(handle);
                var ptr = Marshal.AllocCoTaskMem(data.Length);
                try
                {
                    Marshal.Copy(data, 0, ptr, data.Length);
                    if (!WritePrinter(handle, ptr, data.Length, out var written) || written != data.Length)
                    {
                        throw new IOException($"Envoi incomplet à « {queue} » : {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(ptr);
                }

                EndPagePrinter(handle);
            }
            finally
            {
                EndDocPrinter(handle);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Name = string.Empty;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? OutputFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string DataType = "RAW";
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string printerName, out IntPtr handle, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr handle, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DocInfo doc);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, IntPtr data, int count, out int written);
}
