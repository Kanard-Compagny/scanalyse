namespace Scanalyse.Core.Gs1;

/// <summary>Clé de contrôle modulo 10 des identifiants GS1 (GTIN, SSCC, GLN…).</summary>
public static class CheckDigit
{
    /// <summary>Calcule la clé à ajouter après <paramref name="body"/> (chiffres sans la clé).</summary>
    public static int Compute(ReadOnlySpan<char> body)
    {
        var sum = 0;
        var weight = 3;
        for (var i = body.Length - 1; i >= 0; i--)
        {
            if (!char.IsAsciiDigit(body[i]))
            {
                throw new ArgumentException("Seuls les chiffres sont autorisés.", nameof(body));
            }

            sum += (body[i] - '0') * weight;
            weight = 4 - weight;
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>Vérifie un identifiant complet (clé incluse en dernière position).</summary>
    public static bool IsValid(ReadOnlySpan<char> number)
    {
        if (number.Length < 2)
        {
            return false;
        }

        foreach (var c in number)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return Compute(number[..^1]) == number[^1] - '0';
    }

    /// <summary>Indique si le texte a la forme d'un GTIN (8, 12, 13 ou 14 chiffres).</summary>
    public static bool LooksLikeGtin(ReadOnlySpan<char> text) =>
        text.Length is 8 or 12 or 13 or 14 && !text.ContainsAnyExcept("0123456789");
}
