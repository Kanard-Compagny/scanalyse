using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace Scanalyse.Desktop;

/// <summary>
/// Premier lancement : choix de la langue avant tout le reste (accueil, visite guidée). Le programme démarre ensuite
/// directement dans cette langue, sans redémarrage. Langue proposée d'office : celle de Windows.
/// </summary>
public partial class LanguageWindow : Window
{
    public LanguageWindow()
    {
        InitializeComponent();
        WindowFrame.Attach(this);
        var windowsLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        (windowsLanguage == "fr" ? French : English).IsChecked = true;
        Loaded += (_, _) => (French.IsChecked == true ? French : English).Focus();
    }

    /// <summary>« fr » ou « en ».</summary>
    public string Choice => English.IsChecked == true ? "en" : "fr";

    private void Continue_Click(object sender, RoutedEventArgs e) => Close();

    private void Card_DoubleClick(object sender, MouseButtonEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
