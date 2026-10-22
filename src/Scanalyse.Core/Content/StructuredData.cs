using Scanalyse.Core.Text;

namespace Scanalyse.Core.Content;

/// <summary>Champ d'une section : position, valeur, et sous-champs séparés par &lt;US&gt;.</summary>
/// <param name="Index">Numéro du champ dans sa section (à partir de 1).</param>
/// <param name="Value">Valeur brute du champ.</param>
/// <param name="Parts">Sous-champs (séparateur &lt;US&gt; ou &lt;FS&gt;), s'il y en a plusieurs.</param>
/// <param name="Identifier">Identifiant de données en tête du champ (format 06 : « 1P », « S »…).</param>
/// <param name="IdentifierName">Signification de l'identifiant, si connue.</param>
public sealed record StructuredField(int Index, string Value, IReadOnlyList<string> Parts, string? Identifier = null, string? IdentifierName = null);

/// <summary>Section du contenu (un format ISO/IEC 15434, ou un bloc séparé par &lt;RS&gt;).</summary>
/// <param name="Title">Titre (« Format 01 : données de transport »…).</param>
/// <param name="Fields">Champs séparés par &lt;GS&gt;.</param>
public sealed record StructuredSection(string Title, IReadOnlyList<StructuredField> Fields);

/// <summary>Vue structurée d'un contenu : enveloppe éventuelle et sections.</summary>
/// <param name="Envelope">Description de l'enveloppe (« ISO/IEC 15434 »), ou <c>null</c>.</param>
/// <param name="Sections">Sections dans l'ordre du code.</param>
/// <param name="Remarks">Remarques de structure (fin de transmission absente…).</param>
public sealed record StructuredView(string? Envelope, IReadOnlyList<StructuredSection> Sections, IReadOnlyList<string> Remarks);

/// <summary>
/// Découpe un contenu selon ses séparateurs pour le rendre lisible : enveloppe ISO/IEC 15434 « [)&gt; », formats
/// (&lt;RS&gt;), champs (&lt;GS&gt;) et sous-champs (&lt;US&gt;). Codes transporteurs (UPS, FedEx…), étiquettes
/// de l'industrie (identifiants de données ASC MH10), données de traçabilité.
/// </summary>
public static class StructuredData
{
    private const char Rs = '\u001E';
    private const char Gs = '\u001D';
    private const char Us = '\u001F';
    private const char Fs = '\u001C';
    private const char Eot = '\u0004';

    private static readonly Dictionary<string, string> Formats = new(StringComparer.Ordinal)
    {
        ["01"] = "données de transport (ANSI MH10.8.3, transporteurs)",
        ["02"] = "message EDI complet",
        ["03"] = "EDI ANSI ASC X12",
        ["04"] = "EDI UN/EDIFACT",
        ["05"] = "identifiants d'application GS1",
        ["06"] = "identifiants de données ASC MH10 (DI)",
        ["07"] = "texte libre",
        ["08"] = "CII",
        ["09"] = "données binaires",
        ["12"] = "identifiants d'éléments texte",
    };

    /// <summary>Identifiants de données ASC MH10.8.2 les plus courants sur les étiquettes de l'industrie.</summary>
    private static readonly Dictionary<string, string> DataIdentifiers = new(StringComparer.Ordinal)
    {
        ["P"] = "référence article du client",
        ["1P"] = "référence article du fournisseur",
        ["S"] = "numéro de série",
        ["Q"] = "quantité",
        ["1T"] = "numéro de lot",
        ["K"] = "référence de commande",
        ["V"] = "code fournisseur",
    };

    /// <summary>Vue structurée, ou <c>null</c> si le contenu n'a aucun séparateur (rien à découper).</summary>
    /// <remarks>Accepte le texte brut ou la notation lisible (&lt;GS&gt;…).</remarks>
    public static StructuredView? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // Symboles de contrôle Unicode (␝, ␞, ␄) tels que les affichent certains décodeurs : ramenés aux vrais caractères.
        var pictures = new string([.. text.Select(c => c is >= '\u2400' and <= '\u241F' ? (char)(c - 0x2400) : c == '\u2420' ? ' ' : c)]);
        var t = ControlCharacters.FromVisualized(pictures).TrimEnd('\r', '\n', '\t');
        if (t.IndexOfAny([Rs, Gs, Us, Fs]) < 0)
        {
            return null;
        }

        var remarks = new List<string>();
        var sections = new List<StructuredSection>();
        string? envelope = null;

        if (t.StartsWith("[)>" + Rs, StringComparison.Ordinal))
        {
            envelope = "Enveloppe ISO/IEC 15434 « [)> »";
            var body = t[4..];
            if (body.EndsWith(Eot))
            {
                body = body[..^1];
            }
            else
            {
                remarks.Add("Fin de transmission <EOT> absente : le message est peut-être tronqué.");
            }

            foreach (var block in body.Split(Rs, StringSplitOptions.RemoveEmptyEntries))
            {
                var code = block.Length >= 2 ? block[..2] : block;
                var rest = block.Length > 2 && block[2] == Gs ? block[3..] : block[Math.Min(2, block.Length)..];
                var title = Formats.TryGetValue(code, out var name) ? $"Format {code} : {name}" : $"Format {code}";
                // Formats 05 et 06 : chaque champ commence par son identifiant ; le format 01 est positionnel.
                var fields = rest.Length == 0 ? [] : SplitFields(rest);
                if (code == "06")
                {
                    fields = [.. fields.Select(WithIdentifier)];
                }

                sections.Add(new StructuredSection(title, fields));
            }
        }
        else
        {
            var blocks = t.Split(Rs);
            for (var i = 0; i < blocks.Length; i++)
            {
                if (blocks[i].Length == 0 && blocks.Length > 1)
                {
                    continue;
                }

                sections.Add(new StructuredSection(blocks.Length > 1 ? $"Bloc {i + 1}" : "Contenu", SplitFields(blocks[i])));
            }
        }

        var empty = sections.Sum(s => s.Fields.Count(f => f.Value.Length == 0));
        if (empty > 0)
        {
            remarks.Add($"{empty} champ{(empty > 1 ? "s" : string.Empty)} vide{(empty > 1 ? "s" : string.Empty)} (séparateurs consécutifs) : normal pour un format positionnel, à vérifier sinon.");
        }

        return new StructuredView(envelope, sections, remarks);
    }

    /// <summary>Format 06 : chaque champ commence par un identifiant (0 à 3 chiffres puis une lettre).</summary>
    private static StructuredField WithIdentifier(StructuredField f)
    {
        var n = 0;
        while (n < f.Value.Length && n < 3 && char.IsAsciiDigit(f.Value[n]))
        {
            n++;
        }

        if (n >= f.Value.Length || !char.IsAsciiLetterUpper(f.Value[n]))
        {
            return f;
        }

        var di = f.Value[..(n + 1)];
        return f with { Identifier = di, IdentifierName = DataIdentifiers.GetValueOrDefault(di) };
    }

    private static List<StructuredField> SplitFields(string block)
    {
        var parts = block.Split(Gs);
        var list = new List<StructuredField>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            var sub = parts[i].Split([Us, Fs]);
            list.Add(new StructuredField(i + 1, parts[i], sub.Length > 1 ? sub : []));
        }

        return list;
    }
}
