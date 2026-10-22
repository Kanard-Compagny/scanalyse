using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Scanalyse.Desktop;

/// <summary>
/// Langue de l'interface. Le français est la langue d'écriture du programme ; pour l'anglais, chaque texte affiché
/// (XAML ou posé par le code) est remplacé à la volée d'après la table Lang/en.tsv : texte exact, ou modèle avec
/// parties variables ({0}, {1}…). Un texte absent de la table reste en français.
/// </summary>
internal static partial class Localization
{
    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal);
    private static readonly List<(Regex Pattern, string Replacement)> Patterns = [];
    private static readonly List<(string Prefix, string Replacement)> Prefixes = [];
    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal) { "CodeColors", "LineNumbers" };

    /// <summary>« fr » ou « en ».</summary>
    public static string Language { get; private set; } = "fr";

    public static bool IsEnglish => Language == "en";

    /// <summary>Table complète (français → anglais), pour l'Atelier et les rapports.</summary>
    public static IReadOnlyDictionary<string, string> Table => Exact;

    /// <summary>Lignes de la table telles qu'écrites (modèles compris), pour l'Atelier.</summary>
    public static IReadOnlyList<(string Fr, string En)> Entries { get; private set; } = [];

    public static void Initialize(string language)
    {
        Language = language == "en" ? "en" : "fr";
        if (!IsEnglish)
        {
            return;
        }

        Load();
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), handledEventsToo: true);
    }

    private static void Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Scanalyse.Lang.en.tsv");
        if (stream is null)
        {
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var entries = new List<(string, string)>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab <= 0)
            {
                continue;
            }

            var fr = Unescape(line[..tab]);
            var en = Unescape(line[(tab + 1)..]);
            entries.Add((fr, en));
            if (Slot().IsMatch(fr))
            {
                // Modèle : les parties variables deviennent des groupes, traduits à leur tour.
                var regex = "^" + Slot().Replace(Regex.Escape(fr).Replace("\\{", "{", StringComparison.Ordinal), m => $"(?<s{m.Groups[1].Value}>.+?)") + "$";
                Patterns.Add((new Regex(regex, RegexOptions.Singleline | RegexOptions.CultureInvariant), en));
            }
            else
            {
                Exact[fr] = en;
                if (fr.EndsWith(':') || fr.EndsWith('·'))
                {
                    // Début de message suivi d'un détail (« Rapport non créé : <raison> »).
                    Prefixes.Add((fr + " ", en + " "));
                }
            }
        }

        // Les modèles les plus précis d'abord (plus de texte fixe).
        Patterns.Sort((a, b) => Fixed(b.Pattern).CompareTo(Fixed(a.Pattern)));
        Prefixes.Sort((a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));
        Entries = entries;
    }

    private static int Fixed(Regex r) => Slot2().Replace(r.ToString(), string.Empty).Length;

    private static string Unescape(string s) => s.Replace("\\t", "\t", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);

    /// <summary>Texte dans la langue de l'interface.</summary>
    public static string T(string? text) => text is null || !IsEnglish ? text ?? string.Empty : Translate(text, 0);

    private static string Translate(string text, int depth)
    {
        if (text.Length == 0 || depth > 3)
        {
            return text;
        }

        if (Exact.TryGetValue(text, out var en))
        {
            return en;
        }

        var trimmed = text.Trim();
        if (trimmed.Length != text.Length && Exact.TryGetValue(trimmed, out en))
        {
            return text.Replace(trimmed, en, StringComparison.Ordinal);
        }

        foreach (var (pattern, replacement) in Patterns)
        {
            var m = pattern.Match(text);
            if (m.Success)
            {
                return Slot().Replace(replacement, s => Translate(m.Groups["s" + s.Groups[1].Value].Value, depth + 1));
            }
        }

        foreach (var (prefix, replacement) in Prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                return replacement + Translate(text[prefix.Length..], depth + 1);
            }
        }

        return text;
    }

    // ------------------------------------------------------------------ Interface WPF

    private static readonly DependencyProperty HookedProperty =
        DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(Localization), new PropertyMetadata(false));

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || (bool)element.GetValue(HookedProperty) || Excluded.Contains(element.Name))
        {
            return;
        }

        element.SetValue(HookedProperty, true);
        Apply(element);
        Watch(element, FrameworkElement.ToolTipProperty);
        switch (element)
        {
            case TextBlock:
                Watch(element, TextBlock.TextProperty);
                break;
            case Window:
                Watch(element, Window.TitleProperty);
                break;
            case HeaderedItemsControl:
                Watch(element, HeaderedItemsControl.HeaderProperty);
                break;
            case ContentControl:
                Watch(element, ContentControl.ContentProperty);
                break;
        }
    }

    private static void Watch(FrameworkElement element, DependencyProperty property) =>
        DependencyPropertyDescriptor.FromProperty(property, element.GetType())?.AddValueChanged(element, (_, _) => Apply(element));

    /// <summary>Traduit les textes d'un élément (sans casser les liaisons de données : SetCurrentValue).</summary>
    private static void Apply(FrameworkElement element)
    {
        Set(element, FrameworkElement.ToolTipProperty);
        switch (element)
        {
            case TextBlock tb when tb.Inlines.Count <= 1 || tb.Inlines.All(i => i is System.Windows.Documents.Run):
                Set(element, TextBlock.TextProperty);
                break;
            case Window:
                Set(element, Window.TitleProperty);
                break;
            case HeaderedItemsControl:
                Set(element, HeaderedItemsControl.HeaderProperty);
                break;
            case ContentControl:
                Set(element, ContentControl.ContentProperty);
                break;
        }
    }

    private static void Set(DependencyObject o, DependencyProperty property)
    {
        if (o.GetValue(property) is string { Length: > 0 } text)
        {
            var en = T(text);
            if (!string.Equals(en, text, StringComparison.Ordinal))
            {
                o.SetCurrentValue(property, en);
            }
        }
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Slot();

    [GeneratedRegex(@"\(\?<s\d+>\.\+\?\)")]
    private static partial Regex Slot2();
}
