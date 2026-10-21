using System.Text;

namespace Scanalyse.Core.Text;

/// <summary>
/// Rend visibles les caractères de contrôle (GS, CR, LF…) qu'un scanner transmet sans qu'on les voie.
/// </summary>
public static class ControlCharacters
{
    /// <summary>Séparateur de groupe GS1 (FNC1 transmis).</summary>
    public const char GroupSeparator = '\u001D';

    private static readonly string[] Names =
    [
        "NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "BEL", "BS", "HT", "LF", "VT", "FF", "CR", "SO", "SI",
        "DLE", "DC1", "DC2", "DC3", "DC4", "NAK", "SYN", "ETB", "CAN", "EM", "SUB", "ESC", "FS", "GS", "RS", "US",
    ];

    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        ["NUL"] = "Caractère nul",
        ["STX"] = "Début de texte (préfixe fréquent)",
        ["ETX"] = "Fin de texte (suffixe fréquent)",
        ["EOT"] = "Fin de transmission (séparateur de format 06)",
        ["HT"] = "Tabulation",
        ["LF"] = "Saut de ligne",
        ["CR"] = "Retour chariot (touche Entrée)",
        ["ESC"] = "Échappement",
        ["FS"] = "Séparateur de fichier",
        ["GS"] = "Séparateur de groupe (FNC1 / GS1)",
        ["RS"] = "Séparateur d'enregistrement (format ISO 15434)",
        ["US"] = "Séparateur d'unité",
        ["DEL"] = "Suppression",
    };

    /// <summary>Nom court d'un caractère de contrôle (« GS », « CR »…), ou <c>null</c> si le caractère est imprimable.</summary>
    public static string? NameOf(char c) => c switch
    {
        < ' ' => Names[c],
        '\u007F' => "DEL",
        _ => null,
    };

    /// <summary>Description en français d'un caractère de contrôle, si elle est connue.</summary>
    public static string? Describe(char c)
    {
        var name = NameOf(c);
        return name is not null && Descriptions.TryGetValue(name, out var d) ? d : name;
    }

    /// <summary>Remplace chaque caractère de contrôle par sa forme lisible, par exemple <c>&lt;GS&gt;</c>.</summary>
    public static string Visualize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            var name = NameOf(c);
            if (name is null)
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('<').Append(name).Append('>');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Convertit la notation lisible (<c>&lt;GS&gt;</c>, <c>&lt;CR&gt;</c>…) en vrais caractères de contrôle.
    /// Utile quand un logiciel ou une personne recopie un code « à la main ».
    /// </summary>
    public static string FromVisualized(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains('<'))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '<')
            {
                var end = text.IndexOf('>', i + 1);
                if (end > i + 1 && end - i <= 4)
                {
                    var token = text.Substring(i + 1, end - i - 1);
                    var index = Array.IndexOf(Names, token);
                    if (index >= 0)
                    {
                        sb.Append((char)index);
                        i = end + 1;
                        continue;
                    }

                    if (token == "DEL")
                    {
                        sb.Append('\u007F');
                        i = end + 1;
                        continue;
                    }
                }
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>Liste des caractères de contrôle présents, avec leurs positions (base 0).</summary>
    public static IReadOnlyList<(int Position, char Character, string Name)> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var list = new List<(int, char, string)>();
        for (var i = 0; i < text.Length; i++)
        {
            var name = NameOf(text[i]);
            if (name is not null)
            {
                list.Add((i, text[i], name));
            }
        }

        return list;
    }
}
