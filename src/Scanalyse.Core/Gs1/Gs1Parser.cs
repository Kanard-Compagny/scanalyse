using System.Globalization;
using Scanalyse.Core.Text;

namespace Scanalyse.Core.Gs1;

/// <summary>Un champ GS1 décodé (identifiant d'application + valeur).</summary>
/// <param name="Ai">Code de l'AI.</param>
/// <param name="Value">Valeur brute.</param>
/// <param name="Definition">Définition connue de l'AI, ou <c>null</c>.</param>
/// <param name="Display">Valeur mise en forme pour l'affichage.</param>
/// <param name="Issues">Problèmes détectés sur ce champ.</param>
public sealed record Gs1Element(
    string Ai,
    string Value,
    ApplicationIdentifier? Definition,
    string Display,
    IReadOnlyList<string> Issues)
{
    /// <summary>Libellé du champ.</summary>
    public string Name => Definition?.Name ?? $"Identifiant d'application {Ai} (non répertorié)";

    /// <summary>Date portée par le champ, s'il s'agit d'un AI de date valide.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>Le champ ne présente aucun problème.</summary>
    public bool IsValid => Issues.Count == 0;
}

/// <summary>Forme sous laquelle la chaîne GS1 a été trouvée.</summary>
public enum Gs1Notation
{
    /// <summary>Forme lisible « (01)03453120000011(17)271231 ».</summary>
    Parenthesized,

    /// <summary>Forme transmise par un scanner, avec séparateurs GS.</summary>
    Raw,

    /// <summary>Adresse web GS1 Digital Link.</summary>
    DigitalLink,
}

/// <summary>Résultat de l'analyse d'une chaîne GS1.</summary>
public sealed record Gs1Message(Gs1Notation Notation, IReadOnlyList<Gs1Element> Elements)
{
    /// <summary>Tous les champs sont valides et connus.</summary>
    public bool IsValid => Elements.All(e => e.IsValid && e.Definition is not null);

    /// <summary>Premier champ portant l'AI demandé.</summary>
    public Gs1Element? Get(string ai) => Elements.FirstOrDefault(e => e.Ai == ai);

    /// <summary>
    /// Forme transmise par un scanner : AI et valeurs à la suite, avec un séparateur GS
    /// après chaque champ de longueur variable (sauf le dernier).
    /// </summary>
    public string ToTransmitted()
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < Elements.Count; i++)
        {
            var e = Elements[i];
            sb.Append(e.Ai).Append(e.Value);
            if (i < Elements.Count - 1 && ApplicationIdentifiers.PredefinedLength(e.Ai) is null)
            {
                sb.Append(ControlCharacters.GroupSeparator);
            }
        }

        return sb.ToString();
    }
}

/// <summary>Lecture des chaînes d'éléments GS1.</summary>
public static class Gs1Parser
{
    private static readonly string[] Months =
        ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];

    /// <summary>
    /// Analyse une chaîne GS1, sous forme lisible « (01)… » ou brute avec séparateurs GS
    /// (également « &lt;GS&gt; » en notation texte). L'éventuel identifiant AIM (« ]d2 »…) est ignoré.
    /// </summary>
    /// <returns>Le message décodé, ou <c>null</c> si le texte n'a pas une structure GS1.</returns>
    public static Gs1Message? Parse(string text, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var t = ControlCharacters.FromVisualized(text.Trim('\r', '\n'));
        if (t.Length >= 3 && t[0] == ']')
        {
            t = t[3..];
        }

        if (t.Length > 0 && t[0] == ControlCharacters.GroupSeparator)
        {
            t = t[1..];
        }

        return t.StartsWith('(')
            ? ParseParenthesized(t, clock)
            : ParseRaw(t, clock);
    }

    private static Gs1Message? ParseParenthesized(string t, TimeProvider? clock)
    {
        var elements = new List<Gs1Element>();
        var i = 0;
        while (i < t.Length)
        {
            if (t[i] != '(')
            {
                return null;
            }

            var close = t.IndexOf(')', i);
            if (close < 0)
            {
                return null;
            }

            var ai = t[(i + 1)..close];
            if (ai.Length is < 2 or > 4 || ai.Any(c => !char.IsAsciiDigit(c)))
            {
                return null;
            }

            var next = t.IndexOf('(', close + 1);
            var value = next < 0 ? t[(close + 1)..] : t[(close + 1)..next];
            elements.Add(Describe(ai, value, clock));
            i = next < 0 ? t.Length : next;
        }

        return elements.Count > 0 ? new Gs1Message(Gs1Notation.Parenthesized, elements) : null;
    }

    private static Gs1Message? ParseRaw(string t, TimeProvider? clock)
    {
        if (t.Length < 4 || !char.IsAsciiDigit(t[0]) || !char.IsAsciiDigit(t[1]))
        {
            return null;
        }

        var elements = new List<Gs1Element>();
        var s = t.AsSpan();
        while (!s.IsEmpty)
        {
            if (s[0] == ControlCharacters.GroupSeparator)
            {
                s = s[1..];
                continue;
            }

            var n = ApplicationIdentifiers.AiLength(s);
            var ai = s[..n].ToString();
            if (n < 2 || ai.Any(c => !char.IsAsciiDigit(c)))
            {
                return elements.Count > 0 ? new Gs1Message(Gs1Notation.Raw, elements) : null;
            }

            s = s[n..];
            string value;
            var fixedLength = ApplicationIdentifiers.PredefinedLength(ai);
            if (fixedLength is int len)
            {
                var take = Math.Min(len, s.Length);
                value = s[..take].ToString();
                s = s[take..];
            }
            else
            {
                var gs = s.IndexOf(ControlCharacters.GroupSeparator);
                value = (gs < 0 ? s : s[..gs]).ToString();
                s = gs < 0 ? [] : s[(gs + 1)..];
            }

            elements.Add(Describe(ai, value, clock));
        }

        return elements.Count > 0 ? new Gs1Message(Gs1Notation.Raw, elements) : null;
    }

    /// <summary>Décode et vérifie la valeur d'un AI.</summary>
    public static Gs1Element Describe(string ai, string value, TimeProvider? clock = null)
    {
        var def = ApplicationIdentifiers.Find(ai);
        var issues = new List<string>();
        var display = value;
        DateOnly? date = null;

        var expected = ApplicationIdentifiers.PredefinedLength(ai);
        if (expected is int len && value.Length != len)
        {
            issues.Add($"Longueur incorrecte : {value.Length} caractères au lieu de {len}.");
        }

        if (def is null)
        {
            issues.Add("Identifiant d'application inconnu.");
            return new Gs1Element(ai, value, null, display, issues);
        }

        switch (def.Kind)
        {
            case AiValueKind.Gtin or AiValueKind.Sscc or AiValueKind.Gln:
                if (!CheckDigit.IsValid(value))
                {
                    issues.Add("Clé de contrôle invalide : le numéro a été mal généré ou mal lu.");
                }
                else if (def.Kind is AiValueKind.Gtin && value.Length == 14)
                {
                    display = $"{value} · indicateur {value[0]} · préfixe GS1 : {Gs1Prefixes.Lookup(value.AsSpan(1))}";
                }
                else if (def.Kind is AiValueKind.Sscc && value.Length == 18)
                {
                    display = $"{value} · extension {value[0]} · préfixe GS1 : {Gs1Prefixes.Lookup(value.AsSpan(1))}";
                }

                break;

            case AiValueKind.Date:
                if (TryParseDate(value, clock, out var d, out var endOfMonth))
                {
                    date = d;
                    display = FormatDate(d) + (endOfMonth ? " (fin de mois)" : string.Empty);
                }
                else
                {
                    issues.Add("Date invalide (format attendu : AAMMJJ).");
                }

                break;

            case AiValueKind.DateTime:
                if (value.Length >= 8 && TryParseDate(value[..6], clock, out var dt, out _))
                {
                    date = dt;
                    var minutes = value.Length >= 10 ? value[8..10] : "00";
                    display = $"{FormatDate(dt)} à {value[6..8]} h {minutes}";
                }
                else
                {
                    issues.Add("Date et heure invalides (format attendu : AAMMJJHH[MM]).");
                }

                break;

            case AiValueKind.Decimal:
                if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var raw))
                {
                    display = FormatDecimal(raw, def.Decimals) + (def.Unit is null ? string.Empty : " " + def.Unit);
                }
                else
                {
                    issues.Add("Valeur numérique attendue.");
                }

                break;

            case AiValueKind.DecimalWithCurrency:
                if (value.Length > 3 && long.TryParse(value[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
                {
                    display = $"{FormatDecimal(amount, def.Decimals)} (devise ISO {value[..3]})";
                }
                else
                {
                    issues.Add("Montant attendu : code devise ISO sur 3 chiffres puis montant.");
                }

                break;

            case AiValueKind.Integer:
                if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var q))
                {
                    display = q.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    issues.Add("Nombre entier attendu.");
                }

                break;

            case AiValueKind.Country:
                display = value.Length == 3 ? $"{Countries.Name(value)} ({value})" : value;
                break;

            case AiValueKind.Countries:
                display = string.Join(", ", Enumerable.Range(0, value.Length / 3).Select(k => Countries.Name(value.Substring(k * 3, 3))));
                break;
        }

        return new Gs1Element(ai, value, def, display, issues) { Date = date };
    }

    /// <summary>
    /// Date GS1 AAMMJJ. L'année suit la fenêtre glissante GS1 (-49 / +50 ans) ;
    /// JJ = 00 signifie « dernier jour du mois ».
    /// </summary>
    public static bool TryParseDate(string value, TimeProvider? clock, out DateOnly date, out bool endOfMonth)
    {
        date = default;
        endOfMonth = false;
        if (value.Length != 6 || value.Any(c => !char.IsAsciiDigit(c)))
        {
            return false;
        }

        var yy = int.Parse(value.AsSpan(0, 2), CultureInfo.InvariantCulture);
        var mm = int.Parse(value.AsSpan(2, 2), CultureInfo.InvariantCulture);
        var dd = int.Parse(value.AsSpan(4, 2), CultureInfo.InvariantCulture);
        if (mm is < 1 or > 12)
        {
            return false;
        }

        var now = (clock ?? TimeProvider.System).GetLocalNow().Year;
        var year = 2000 + yy;
        if (year > now + 50)
        {
            year -= 100;
        }
        else if (year < now - 49)
        {
            year += 100;
        }

        var days = DateTime.DaysInMonth(year, mm);
        if (dd == 0)
        {
            endOfMonth = true;
            dd = days;
        }

        if (dd > days)
        {
            return false;
        }

        date = new DateOnly(year, mm, dd);
        return true;
    }

    /// <summary>Date au format français « 31 décembre 2027 ».</summary>
    public static string FormatDate(DateOnly d) => $"{d.Day} {Months[d.Month - 1]} {d.Year}";

    private static string FormatDecimal(long raw, int decimals)
    {
        if (decimals <= 0)
        {
            return raw.ToString(CultureInfo.InvariantCulture);
        }

        var divisor = (decimal)Math.Pow(10, decimals);
        return (raw / divisor).ToString("F" + decimals, CultureInfo.InvariantCulture).Replace('.', ',');
    }
}
