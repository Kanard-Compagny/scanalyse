using System.Text;

namespace Scanalyse.Core.Diagnostics;

/// <summary>
/// Correspondance des touches entre un clavier américain (QWERTY US) et un clavier français (AZERTY).
/// Sert à reconnaître un scanner en mode clavier réglé sur la mauvaise langue.
/// </summary>
public static class KeyboardLayouts
{
    // Chaque paire : caractère produit par une touche sur un clavier US, puis sur un clavier FR (même touche, même état Maj).
    private static readonly (char Us, char Fr)[] Pairs =
    [
        ('`', '²'), ('1', '&'), ('2', 'é'), ('3', '"'), ('4', '\''), ('5', '('), ('6', '-'), ('7', 'è'), ('8', '_'), ('9', 'ç'), ('0', 'à'), ('-', ')'), ('=', '='),
        ('!', '1'), ('@', '2'), ('#', '3'), ('$', '4'), ('%', '5'), ('^', '6'), ('&', '7'), ('*', '8'), ('(', '9'), (')', '0'), ('_', '°'), ('+', '+'),
        ('q', 'a'), ('w', 'z'), ('a', 'q'), ('z', 'w'), ('m', ','), (';', 'm'), ('\'', 'ù'), (',', ';'), ('.', ':'), ('/', '!'),
        ('Q', 'A'), ('W', 'Z'), ('A', 'Q'), ('Z', 'W'), ('M', '?'), (':', 'M'), ('"', '%'), ('<', '.'), ('>', '/'), ('?', '§'),
        (']', '$'), ('}', '£'), ('\\', '*'), ('|', 'µ'),
    ];

    private static readonly Dictionary<char, char> UsToFr = Pairs.ToDictionary(p => p.Us, p => p.Fr);
    private static readonly Dictionary<char, char> FrToUs = Pairs.GroupBy(p => p.Fr).ToDictionary(g => g.Key, g => g.First().Us);

    /// <summary>
    /// Ce qu'affiche un PC réglé en AZERTY quand le scanner « tape » <paramref name="text"/> en pensant être sur un clavier US.
    /// </summary>
    public static string ScannerUsOnPcFr(string text) => Translate(text, UsToFr);

    /// <summary>
    /// Ce qu'affiche un PC réglé en QWERTY US quand le scanner « tape » <paramref name="text"/> en pensant être sur un clavier AZERTY.
    /// </summary>
    public static string ScannerFrOnPcUs(string text) => Translate(text, FrToUs);

    private static string Translate(string text, Dictionary<char, char> map)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(map.TryGetValue(c, out var m) ? m : c);
        }

        return sb.ToString();
    }
}
