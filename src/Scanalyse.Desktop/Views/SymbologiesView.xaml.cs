using System.Windows;
using System.Windows.Controls;
using Scanalyse.Core.Symbologies;

namespace Scanalyse.Desktop.Views;

/// <summary>Référentiel des symbologies (données issues du Core), avec recherche et catégories.</summary>
public partial class SymbologiesView : UserControl
{
    private readonly List<SymbologyCard> _all;

    /// <summary>Largeur d'une fiche : autant de colonnes que la place le permet (fiches de 320 à 460 points).</summary>
    public static readonly DependencyProperty CardWidthProperty =
        DependencyProperty.Register(nameof(CardWidth), typeof(double), typeof(SymbologiesView), new PropertyMetadata(380.0));

    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    private void List_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double gap = 16;
        var available = e.NewSize.Width - 2;
        if (available <= 0)
        {
            return;
        }

        var columns = Math.Max(1, (int)((available + gap) / (320 + gap)));
        CardWidth = Math.Floor(Math.Min(460, (available + gap) / columns - gap));
    }

    public SymbologiesView()
    {
        InitializeComponent();
        HintAligner.Attach(SearchBox, SearchPlaceholder);
        _all = Symbologies.All
            .OrderBy(s => s.Family)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .Select(SymbologyCard.From)
            .ToList();
        ApplyFilter();
    }

    /// <summary>L'utilisateur veut créer un code de ce type (identifiant de type de l'Atelier).</summary>
    public event Action<string>? CreateRequested;

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (_all is null || List is null || SearchBox is null || Categories is null)
        {
            return;
        }

        var query = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var cat = Categories.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "all";

        var items = _all.Where(c => cat switch
        {
            "all" => true,
            "gs1" => c.SupportsGs1,
            _ => c.FamilyKey == cat,
        });
        if (query.Length > 0)
        {
            items = items.Where(c => c.SearchText.Contains(Normalize(query), StringComparison.Ordinal));
        }

        var list = items.ToList();
        List.ItemsSource = list;
        CountText.Text = list.Count == 0
            ? "Aucune symbologie ne correspond."
            : $"{list.Count} symbologie{(list.Count > 1 ? "s" : string.Empty)} sur {_all.Count}";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && id.Length > 0)
        {
            CreateRequested?.Invoke(id);
        }
    }

    /// <summary>Minuscules sans accents, pour une recherche tolérante.</summary>
    internal static string Normalize(string s)
    {
        var d = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }
}

/// <summary>Fiche d'une symbologie prête à afficher.</summary>
public sealed record SymbologyCard(string Name, string Family, string FamilyKey, string CharacterSet, string Capacity, string TypicalUses,
    string Standard, string QuietZone, bool SupportsGs1, string TypeId)
{
    public Visibility Gs1Visibility => SupportsGs1 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CreateVisibility => TypeId.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Texte de recherche (sans accents).</summary>
    public string SearchText => SymbologiesView.Normalize($"{Name} {Family} {CharacterSet} {Capacity} {TypicalUses} {Standard}");

    public static SymbologyCard From(SymbologyInfo s) => new(
        s.Name,
        s.Family switch
        {
            SymbologyFamily.Matrix => "2D matriciel",
            SymbologyFamily.Stacked => "2D empilé",
            SymbologyFamily.Retail => "Distribution",
            _ => "Industriel",
        },
        s.Family.ToString(),
        s.CharacterSet,
        s.Capacity,
        s.TypicalUses,
        s.Standard,
        s.QuietZone,
        s.SupportsGs1,
        CreatorType(s.Symbology));

    /// <summary>Type correspondant dans « Créer un code » (vide si la création n'est pas proposée).</summary>
    private static string CreatorType(Symbology s) => s switch
    {
        Symbology.QrCode => "qr",
        Symbology.MicroQrCode => "microqr",
        Symbology.RmqrCode => "rmqr",
        Symbology.DataMatrix => "datamatrix",
        Symbology.Aztec => "aztec",
        Symbology.Pdf417 => "pdf417",
        Symbology.Ean13 or Symbology.Isbn => "ean13",
        Symbology.Ean8 => "ean8",
        Symbology.UpcA => "upca",
        Symbology.UpcE => "upce",
        Symbology.DataBar => "databar",
        Symbology.DataBarExpanded => "databarexp",
        Symbology.Code128 => "code128",
        Symbology.Code39 => "code39",
        Symbology.Code93 => "code93",
        Symbology.Codabar => "codabar",
        Symbology.Itf => "itf",
        Symbology.Itf14 => "itf14",
        Symbology.MicroPdf417 => "micropdf417",
        Symbology.MaxiCode => "maxicode",
        Symbology.DataBarLimited => "databarltd",
        Symbology.Code32 => "code32",
        Symbology.Pzn => "pzn",
        _ => string.Empty,
    };
}
