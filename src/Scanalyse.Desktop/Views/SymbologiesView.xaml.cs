using System.Windows;
using System.Windows.Controls;
using Scanalyse.Core.Symbologies;

namespace Scanalyse.Desktop.Views;

/// <summary>Référentiel des symbologies (données issues du Core).</summary>
public partial class SymbologiesView : UserControl
{
    public SymbologiesView()
    {
        InitializeComponent();
        List.ItemsSource = Symbologies.All
            .OrderBy(s => s.Family)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .Select(SymbologyCard.From)
            .ToList();
    }
}

/// <summary>Fiche d'une symbologie prête à afficher.</summary>
public sealed record SymbologyCard(string Name, string Family, string CharacterSet, string Capacity, string TypicalUses,
    string Standard, string QuietZone, Visibility Gs1Visibility)
{
    public static SymbologyCard From(SymbologyInfo s) => new(
        s.Name,
        s.Family switch
        {
            SymbologyFamily.Matrix => "2D matriciel",
            SymbologyFamily.Stacked => "2D empilé",
            SymbologyFamily.Retail => "Distribution",
            _ => "Industriel",
        },
        s.CharacterSet,
        s.Capacity,
        s.TypicalUses,
        s.Standard,
        s.QuietZone,
        s.SupportsGs1 ? Visibility.Visible : Visibility.Collapsed);
}
