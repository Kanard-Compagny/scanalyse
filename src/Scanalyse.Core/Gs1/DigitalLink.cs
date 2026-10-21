namespace Scanalyse.Core.Gs1;

/// <summary>
/// Adresses web GS1 Digital Link (https://exemple.com/01/09506000134352/10/LOT?17=271231) :
/// une même étiquette sert au passage en caisse et à une page web produit.
/// </summary>
public static class DigitalLink
{
    private static readonly HashSet<string> PrimaryKeys = new(StringComparer.Ordinal)
    {
        "01", "00", "253", "255", "401", "402", "414", "417", "8003", "8004", "8006", "8010", "8013", "8017", "8018",
    };

    /// <summary>Extrait les champs GS1 d'une adresse Digital Link, ou renvoie <c>null</c>.</summary>
    public static Gs1Message? TryParse(Uri uri, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var start = Array.FindIndex(segments, s => PrimaryKeys.Contains(s));
        if (start < 0 || start + 1 >= segments.Length)
        {
            return null;
        }

        var elements = new List<Gs1Element>();
        for (var i = start; i + 1 < segments.Length; i += 2)
        {
            var ai = segments[i];
            if (ai.Length is < 2 or > 4 || ai.Any(c => !char.IsAsciiDigit(c)))
            {
                break;
            }

            elements.Add(Gs1Parser.Describe(ai, Uri.UnescapeDataString(segments[i + 1]), clock));
        }

        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = pair[..eq];
            if (key.Length is >= 2 and <= 4 && key.All(char.IsAsciiDigit))
            {
                elements.Add(Gs1Parser.Describe(key, Uri.UnescapeDataString(pair[(eq + 1)..]), clock));
            }
        }

        return elements.Count > 0 ? new Gs1Message(Gs1Notation.DigitalLink, elements) : null;
    }
}
