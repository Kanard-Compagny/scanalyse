using System.Globalization;
using System.Text;
using Scanalyse.Core.Content;

namespace Scanalyse.Core.Printing;

/// <summary>Action de maintenance envoyée à une imprimante d'étiquettes.</summary>
public enum PrinterAction
{
    /// <summary>Lire l'état (papier, ruban, tête, pause…).</summary>
    Status,

    /// <summary>Calibrer le capteur d'étiquettes (détection de l'espace ou de la marque noire).</summary>
    Calibrate,

    /// <summary>Imprimer l'étiquette de configuration.</summary>
    ConfigLabel,

    /// <summary>Redémarrer l'imprimante (réinitialisation logicielle, réglages conservés).</summary>
    Reset,
}

/// <summary>
/// Commandes de maintenance par langage (ZPL, TSPL, EPL2, DPL) et lecture des réponses d'état. La réponse n'arrive que par
/// le réseau (port 9100) ou un port série : une file d'impression Windows ne renvoie rien.
/// </summary>
public static class PrinterCommands
{
    private const char Esc = '\u001B';
    private const char Soh = '\u0001';
    private const char Stx = '\u0002';

    /// <summary>Octets à envoyer pour cette action ; <c>null</c> si le langage ne la propose pas.</summary>
    /// <param name="language">Langage de l'imprimante.</param>
    /// <param name="action">Action voulue.</param>
    public static byte[]? Command(PrinterLanguage language, PrinterAction action)
    {
        var text = (language, action) switch
        {
            (PrinterLanguage.Zpl, PrinterAction.Status) => "~HS",
            (PrinterLanguage.Zpl, PrinterAction.Calibrate) => "~JC",
            (PrinterLanguage.Zpl, PrinterAction.ConfigLabel) => "~WC",
            (PrinterLanguage.Zpl, PrinterAction.Reset) => "~JR",
            (PrinterLanguage.Tspl, PrinterAction.Status) => Esc + "!?",
            (PrinterLanguage.Tspl, PrinterAction.Calibrate) => "GAPDETECT\r\n",
            (PrinterLanguage.Tspl, PrinterAction.ConfigLabel) => "SELFTEST\r\n",
            (PrinterLanguage.Tspl, PrinterAction.Reset) => Esc + "!R",
            (PrinterLanguage.Epl2, PrinterAction.Status) => "\r\nUQ\r\n",
            (PrinterLanguage.Epl2, PrinterAction.Calibrate) => "\r\nxa\r\n",
            (PrinterLanguage.Epl2, PrinterAction.ConfigLabel) => "\r\nU\r\n",
            (PrinterLanguage.Epl2, PrinterAction.Reset) => "\r\n^@\r\n",
            (PrinterLanguage.Dpl, PrinterAction.Status) => Soh + "A",
            (PrinterLanguage.Dpl, PrinterAction.ConfigLabel) => Stx + "Z",
            (PrinterLanguage.Dpl, PrinterAction.Reset) => Soh + "#",
            _ => null,
        };
        return text is null ? null : Encoding.ASCII.GetBytes(text);
    }

    /// <summary>Commande lisible, pour l'afficher (caractères de contrôle notés &lt;ESC&gt;, &lt;SOH&gt;…).</summary>
    /// <param name="language">Langage de l'imprimante.</param>
    /// <param name="action">Action voulue.</param>
    public static string Display(PrinterLanguage language, PrinterAction action) =>
        Command(language, action) is { } bytes
            ? Encoding.ASCII.GetString(bytes).Replace("\u001B", "<ESC>", StringComparison.Ordinal).Replace("\u0001", "<SOH>", StringComparison.Ordinal)
                .Replace("\u0002", "<STX>", StringComparison.Ordinal).Replace("\r\n", " ", StringComparison.Ordinal).Trim()
            : string.Empty;

    /// <summary>Traduit la réponse d'état de l'imprimante en constats.</summary>
    /// <param name="language">Langage de l'imprimante.</param>
    /// <param name="reply">Octets reçus.</param>
    public static IReadOnlyList<Alert> ParseStatus(PrinterLanguage language, byte[] reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.Length == 0)
        {
            return [new Alert(Severity.Warning, "Aucune réponse : l'imprimante ne renvoie pas son état sur cette connexion, ou le langage choisi n'est pas le sien.")];
        }

        return language switch
        {
            PrinterLanguage.Zpl => Zpl(Encoding.ASCII.GetString(reply)),
            PrinterLanguage.Tspl => Tspl(reply[^1]),
            PrinterLanguage.Dpl => Dpl(Encoding.ASCII.GetString(reply)),
            _ => [.. Encoding.ASCII.GetString(reply).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => new Alert(Severity.Info, l))],
        };
    }

    /// <summary>~HS : trois chaînes &lt;STX&gt;…&lt;ETX&gt; de valeurs séparées par des virgules.</summary>
    private static List<Alert> Zpl(string text)
    {
        var strings = text.Split('\u0002').Select(s => s.Split('\u0003')[0].Trim()).Where(s => s.Contains(',', StringComparison.Ordinal)).ToList();
        if (strings.Count < 2)
        {
            return [new Alert(Severity.Warning, "Réponse d'état illisible : « " + text.Trim() + " ».")];
        }

        var a = strings[0].Split(',');
        var b = strings[1].Split(',');
        string A(int k) => k < a.Length ? a[k].Trim() : string.Empty;
        string B(int k) => k < b.Length ? b[k].Trim() : string.Empty;
        var list = new List<Alert>();
        if (A(1) == "1")
        {
            list.Add(new Alert(Severity.Error, "Plus de papier (ou étiquette non détectée)."));
        }

        if (B(2) == "1")
        {
            list.Add(new Alert(Severity.Error, "Tête d'impression ouverte."));
        }

        if (B(3) == "1")
        {
            list.Add(new Alert(Severity.Error, "Ruban terminé ou absent."));
        }

        if (A(2) == "1")
        {
            list.Add(new Alert(Severity.Warning, "Imprimante en pause."));
        }

        if (A(5) == "1")
        {
            list.Add(new Alert(Severity.Warning, "Mémoire de réception pleine."));
        }

        if (A(9) == "1")
        {
            list.Add(new Alert(Severity.Error, "Mémoire corrompue (réglages perdus)."));
        }

        if (A(10) == "1" || A(11) == "1")
        {
            list.Add(new Alert(Severity.Warning, A(10) == "1" ? "Tête trop froide." : "Tête trop chaude."));
        }

        if (list.Count == 0)
        {
            list.Add(new Alert(Severity.Ok, "Prête : papier, ruban et tête sans défaut."));
        }

        list.Add(new Alert(Severity.Info, B(4) == "1" ? "Mode transfert thermique (avec ruban)." : "Mode thermique direct (sans ruban)."));
        if (int.TryParse(A(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) && length > 0)
        {
            list.Add(new Alert(Severity.Info, $"Longueur d'étiquette mémorisée : {length} points."));
        }

        if (int.TryParse(A(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var formats) && formats > 0)
        {
            list.Add(new Alert(Severity.Info, $"{formats} étiquette(s) en attente dans la mémoire."));
        }

        if (long.TryParse(B(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining) && remaining > 0)
        {
            list.Add(new Alert(Severity.Info, $"{remaining} étiquette(s) restant à imprimer dans le lot en cours."));
        }

        return list;
    }

    /// <summary>&lt;ESC&gt;!? : un octet, un bit par défaut.</summary>
    private static List<Alert> Tspl(byte status)
    {
        var list = new List<Alert>();
        (int Bit, Severity Severity, string Text)[] bits =
        [
            (0x01, Severity.Error, "Tête d'impression ouverte."),
            (0x02, Severity.Error, "Bourrage papier."),
            (0x04, Severity.Error, "Plus de papier."),
            (0x08, Severity.Error, "Ruban terminé ou absent."),
            (0x10, Severity.Warning, "Imprimante en pause."),
            (0x20, Severity.Info, "Impression en cours."),
            (0x40, Severity.Error, "Capot ouvert."),
            (0x80, Severity.Error, "Autre erreur (voir l'écran ou les voyants de l'imprimante)."),
        ];
        foreach (var (bit, severity, text) in bits)
        {
            if ((status & bit) != 0)
            {
                list.Add(new Alert(severity, text));
            }
        }

        if (list.Count == 0)
        {
            list.Add(new Alert(Severity.Ok, "Prête : aucun défaut signalé."));
        }

        return list;
    }

    /// <summary>&lt;SOH&gt;A : huit lettres Y/N.</summary>
    private static List<Alert> Dpl(string text)
    {
        var flags = new string([.. text.Where(c => c is 'Y' or 'N')]);
        if (flags.Length < 7)
        {
            return [new Alert(Severity.Warning, "Réponse d'état illisible : « " + text.Trim() + " ».")];
        }

        var list = new List<Alert>();
        (int Index, Severity Severity, string Text)[] meaning =
        [
            (1, Severity.Error, "Papier terminé ou défaut papier."),
            (2, Severity.Error, "Ruban terminé ou défaut ruban."),
            (5, Severity.Warning, "Imprimante en pause."),
            (6, Severity.Info, "Étiquette présentée (en attente de retrait)."),
            (0, Severity.Info, "Interpréteur occupé (préparation d'une étiquette)."),
            (4, Severity.Info, "Impression en cours."),
        ];
        foreach (var (index, severity, msg) in meaning)
        {
            if (index < flags.Length && flags[index] == 'Y')
            {
                list.Add(new Alert(severity, msg));
            }
        }

        if (!list.Any(a => a.Severity is Severity.Error or Severity.Warning))
        {
            list.Insert(0, new Alert(Severity.Ok, "Prête : papier et ruban sans défaut."));
        }

        return list;
    }
}
