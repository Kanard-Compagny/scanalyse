using System.Text.RegularExpressions;

namespace Scanalyse.Core.Printing;

/// <summary>Catégorie d'un morceau de fichier de configuration, pour la coloration de l'éditeur.</summary>
public enum ConfigTokenKind
{
    /// <summary>Texte sans rôle particulier (espaces, ponctuation).</summary>
    Plain,

    /// <summary>Commande reconnue (^MD, ~SD, ! U1, SIZE, SETUP, &lt;ESC&gt;…).</summary>
    Command,

    /// <summary>Verbe SGD (setvar, getvar, do).</summary>
    Verb,

    /// <summary>Nom de réglage (SGD, clé JSON).</summary>
    Name,

    /// <summary>Valeur entre guillemets ou données de champ.</summary>
    Value,

    /// <summary>Paramètres d'une commande.</summary>
    Parameter,

    /// <summary>Commentaire (^FX).</summary>
    Comment,

    /// <summary>Commande ou mot inconnu de l'imprimante (faute de frappe, mauvais préfixe…).</summary>
    Unknown,
}

/// <summary>Morceau coloré du fichier.</summary>
/// <param name="Start">Position du premier caractère dans le texte.</param>
/// <param name="Length">Nombre de caractères.</param>
/// <param name="Kind">Catégorie.</param>
public readonly record struct ConfigToken(int Start, int Length, ConfigTokenKind Kind);

public static partial class PrinterConfig
{
    /// <summary>
    /// Découpe un fichier de configuration pour le colorer, ligne par ligne et quel que soit le langage : SGD, JSON SGD,
    /// ZPL (commande inconnue en rouge), notations &lt;STX&gt; / &lt;ESC&gt;, et lignes TSPL / EPL2 / DPL / Fingerprint.
    /// Les morceaux couvrent tout le texte, retours à la ligne compris, dans l'ordre.
    /// </summary>
    public static IReadOnlyList<ConfigToken> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<ConfigToken>();
        var pos = 0;
        while (pos < text.Length)
        {
            var end = text.IndexOf('\n', pos);
            var lineEnd = end < 0 ? text.Length : end;
            var contentEnd = lineEnd > pos && text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            TokenizeLine(text, pos, contentEnd, tokens);
            if (contentEnd < (end < 0 ? text.Length : end + 1))
            {
                Add(tokens, contentEnd, (end < 0 ? text.Length : end + 1) - contentEnd, ConfigTokenKind.Plain);
            }

            pos = end < 0 ? text.Length : end + 1;
        }

        return tokens;
    }

    private static void Add(List<ConfigToken> tokens, int start, int length, ConfigTokenKind kind)
    {
        if (length <= 0)
        {
            return;
        }

        // Morceaux voisins de même catégorie fusionnés : moins d'éléments à dessiner.
        if (tokens.Count > 0 && tokens[^1].Kind == kind && tokens[^1].Start + tokens[^1].Length == start)
        {
            tokens[^1] = tokens[^1] with { Length = tokens[^1].Length + length };
            return;
        }

        tokens.Add(new ConfigToken(start, length, kind));
    }

    private static void TokenizeLine(string text, int start, int end, List<ConfigToken> tokens)
    {
        var line = text[start..end];
        var trimmed = line.TrimStart();
        var indent = line.Length - trimmed.Length;
        Add(tokens, start, indent, ConfigTokenKind.Plain);
        var at = start + indent;
        if (trimmed.Length == 0)
        {
            return;
        }

        if (trimmed[0] == '!')
        {
            SgdLine(trimmed, at, tokens);
        }
        else if (trimmed[0] == '{')
        {
            Quoted(trimmed, 0, at, tokens, json: true);
        }
        else if (trimmed[0] is '^' or '~')
        {
            ZplLine(trimmed, at, tokens);
        }
        else if (ControlNotation().IsMatch(trimmed) || trimmed[0] is '\u0002' or '\u0001' or '\u001B')
        {
            ControlLine(trimmed, at, tokens);
        }
        else
        {
            WordLine(trimmed, at, tokens);
        }
    }

    private static void SgdLine(string s, int at, List<ConfigToken> tokens)
    {
        var m = SgdHead().Match(s);
        if (!m.Success)
        {
            Add(tokens, at, s.Length, ConfigTokenKind.Unknown);
            return;
        }

        var head = m.Groups[1];
        var wellFormed = Regex.IsMatch(head.Value, @"^!\s+U1$", RegexOptions.CultureInvariant);
        Add(tokens, at, head.Length, wellFormed ? ConfigTokenKind.Command : ConfigTokenKind.Unknown);
        Add(tokens, at + head.Length, m.Groups[2].Length, ConfigTokenKind.Plain);
        var verb = m.Groups[3];
        Add(tokens, at + verb.Index, verb.Length, verb.Value is "setvar" or "getvar" or "do" ? ConfigTokenKind.Verb : ConfigTokenKind.Unknown);
        Quoted(s, verb.Index + verb.Length, at, tokens, json: false);
    }

    /// <summary>Chaînes entre guillemets : nom puis valeur (SGD), ou clé (suivie de « : ») puis valeur (JSON).</summary>
    private static void Quoted(string s, int from, int at, List<ConfigToken> tokens, bool json)
    {
        var index = 0;
        var i = from;
        while (i < s.Length)
        {
            var c = s[i];
            if (c is '"' or '“' or '”')
            {
                var close = i + 1;
                while (close < s.Length && s[close] is not ('"' or '“' or '”'))
                {
                    close++;
                }

                var length = Math.Min(s.Length, close + 1) - i;
                ConfigTokenKind kind;
                if (c != '"' || (close < s.Length && s[close] != '"'))
                {
                    kind = ConfigTokenKind.Unknown; // guillemets typographiques
                }
                else if (json)
                {
                    var next = i + length;
                    while (next < s.Length && s[next] == ' ')
                    {
                        next++;
                    }

                    kind = next < s.Length && s[next] == ':' ? ConfigTokenKind.Name : ConfigTokenKind.Value;
                }
                else
                {
                    kind = index == 0 ? ConfigTokenKind.Name : ConfigTokenKind.Value;
                }

                Add(tokens, at + i, length, kind);
                index++;
                i += length;
                continue;
            }

            var startPlain = i;
            while (i < s.Length && s[i] is not ('"' or '“' or '”'))
            {
                i++;
            }

            var chunk = s[startPlain..i];
            Add(tokens, at + startPlain, i - startPlain, json && chunk.Any(char.IsAsciiLetter) && !chunk.Contains("null", StringComparison.Ordinal)
                ? ConfigTokenKind.Value : ConfigTokenKind.Plain);
        }
    }

    private static void ZplLine(string s, int at, List<ConfigToken> tokens)
    {
        var last = 0;
        foreach (Match m in ZplToken().Matches(s))
        {
            Add(tokens, at + last, m.Index - last, ConfigTokenKind.Plain);
            var prefix = m.Groups[1].Value[0];
            var code = m.Groups[2].Value.ToUpperInvariant();
            var head = 1 + m.Groups[2].Length;
            Add(tokens, at + m.Index, head, ZplKnown(prefix, code, out _, out _) ? ConfigTokenKind.Command : ConfigTokenKind.Unknown);
            var argKind = (prefix, code) switch
            {
                ('^', "FX") => ConfigTokenKind.Comment,
                ('^', "FD") or ('^', "FV") => ConfigTokenKind.Value,
                _ => ConfigTokenKind.Parameter,
            };
            Add(tokens, at + m.Index + head, m.Length - head, argKind);
            last = m.Index + m.Length;
        }

        Add(tokens, at + last, s.Length - last, ConfigTokenKind.Plain);
    }

    private static void ControlLine(string s, int at, List<ConfigToken> tokens)
    {
        var last = 0;
        foreach (Match m in ControlCode().Matches(s))
        {
            Add(tokens, at + last, m.Index - last, ConfigTokenKind.Parameter);
            Add(tokens, at + m.Index, m.Length, ConfigTokenKind.Command);
            last = m.Index + m.Length;
        }

        WordParameters(s, last, at, tokens);
    }

    /// <summary>TSPL, EPL2, DPL, Fingerprint : premier mot (et le réglage après SET / SETUP) en commande, le reste en paramètres.</summary>
    private static void WordLine(string s, int at, List<ConfigToken> tokens)
    {
        var word = 0;
        while (word < s.Length && (char.IsAsciiLetterOrDigit(s[word]) || s[word] is '_' or '$'))
        {
            word++;
        }

        if (word == 0)
        {
            Add(tokens, at, s.Length, ConfigTokenKind.Plain);
            return;
        }

        Add(tokens, at, word, ConfigTokenKind.Command);
        var rest = word;
        if (s[..word].Equals("SET", StringComparison.OrdinalIgnoreCase))
        {
            var space = rest;
            while (space < s.Length && s[space] == ' ')
            {
                space++;
            }

            var next = space;
            while (next < s.Length && (char.IsAsciiLetterOrDigit(s[next]) || s[next] == '_'))
            {
                next++;
            }

            Add(tokens, at + rest, space - rest, ConfigTokenKind.Plain);
            Add(tokens, at + space, next - space, ConfigTokenKind.Verb);
            rest = next;
        }

        WordParameters(s, rest, at, tokens);
    }

    private static void WordParameters(string s, int from, int at, List<ConfigToken> tokens)
    {
        var i = from;
        while (i < s.Length)
        {
            if (s[i] == '"')
            {
                var close = s.IndexOf('"', i + 1);
                var length = (close < 0 ? s.Length : close + 1) - i;
                Add(tokens, at + i, length, ConfigTokenKind.Value);
                i += length;
                continue;
            }

            var start = i;
            while (i < s.Length && s[i] != '"')
            {
                i++;
            }

            Add(tokens, at + start, i - start, ConfigTokenKind.Parameter);
        }
    }

    [GeneratedRegex(@"^(!\s*U1?)(\s*)([A-Za-z]*)", RegexOptions.CultureInvariant)]
    private static partial Regex SgdHead();

    [GeneratedRegex(@"<(STX|SOH|ESC|ETX|CR|LF)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ControlNotation();

    [GeneratedRegex(@"<(STX|SOH|ESC|ETX|CR|LF)>|[\u0001\u0002\u0003\u001B]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ControlCode();
}
