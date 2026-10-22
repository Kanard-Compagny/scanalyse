using System.Diagnostics;
using System.IO;

namespace Scanalyse.Desktop.Relay;

/// <summary>Terminal Android vu par ADB.</summary>
/// <param name="Serial">Numéro de série ADB.</param>
/// <param name="State">« device », « unauthorized », « offline »…</param>
/// <param name="Model">Modèle (TC52, CT40, Memor 11…).</param>
public sealed record AdbDevice(string Serial, string State, string Model)
{
    public bool Ready => State == "device";

    public string StateText => State switch
    {
        "device" => "prêt",
        "unauthorized" => "à autoriser : accepte le débogage USB sur l'écran du terminal",
        "offline" => "hors ligne : débranche et rebranche le câble",
        _ => State,
    };
}

/// <summary>
/// Pilote adb.exe (Android Platform Tools) : liste des terminaux, redirection des ports du relais, ouverture de la page.
/// Scanalyse n'embarque pas ADB : il utilise celui déjà présent sur le PC (outils SAV, Android Studio…).
/// </summary>
internal static class Adb
{
    /// <summary>adb.exe trouvé : réglage, PATH, emplacements habituels. <c>null</c> si absent.</summary>
    public static string? Find(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var candidates = new List<string>();
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(dir.Trim('"'), "adb.exe"));
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        candidates.AddRange(
        [
            Path.Combine(local, "Android", "Sdk", "platform-tools", "adb.exe"),
            Path.Combine(programs, "Android", "platform-tools", "adb.exe"),
            @"C:\platform-tools\adb.exe",
            @"C:\adb\adb.exe",
            @"C:\Android\platform-tools\adb.exe",
            Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe"),
        ]);
        return candidates.FirstOrDefault(c =>
        {
            try
            {
                return File.Exists(c);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                return false;
            }
        });
    }

    /// <summary>Lance adb avec un délai maximum ; renvoie le code de sortie et la sortie texte.</summary>
    public static async Task<(int Code, string Output)> RunAsync(string adb, string arguments, int timeoutMs = 15000)
    {
        var psi = new ProcessStartInfo(adb, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("adb n'a pas démarré.");
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            return (-1, "adb ne répond pas (délai dépassé).");
        }

        return (p.ExitCode, ((await output) + (await error)).Trim());
    }

    /// <summary>Terminaux branchés (« adb devices -l »).</summary>
    public static async Task<IReadOnlyList<AdbDevice>> DevicesAsync(string adb)
    {
        var (_, output) = await RunAsync(adb, "devices -l");
        var list = new List<AdbDevice>();
        foreach (var line in output.Split('\n').Select(l => l.Trim()))
        {
            if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.Ordinal) || line.StartsWith('*'))
            {
                continue;
            }

            var parts = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var model = parts.FirstOrDefault(p => p.StartsWith("model:", StringComparison.Ordinal))?["model:".Length..].Replace('_', ' ') ?? "Terminal Android";
            list.Add(new AdbDevice(parts[0], parts[1], model));
        }

        return list;
    }

    /// <summary>
    /// Relie le terminal au relais par le câble USB : ses ports 8765/8766 renvoient vers ceux du PC,
    /// puis la page de relais s'ouvre dans son navigateur.
    /// </summary>
    public static async Task<string?> OpenRelayAsync(string adb, string serial)
    {
        foreach (var port in new[] { RelayServer.HttpPort, RelayServer.RawPort })
        {
            var (code, output) = await RunAsync(adb, $"-s \"{serial}\" reverse tcp:{port} tcp:{port}");
            if (code != 0)
            {
                return $"Redirection du port {port} refusée : {output}";
            }
        }

        var (c2, o2) = await RunAsync(adb, $"-s \"{serial}\" shell am start -a android.intent.action.VIEW -d http://127.0.0.1:{RelayServer.HttpPort}/relais");
        return c2 == 0 && !o2.Contains("Error", StringComparison.OrdinalIgnoreCase) ? null : $"Ouverture du navigateur impossible : {o2}";
    }
}
