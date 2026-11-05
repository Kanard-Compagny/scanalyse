using System.Windows;
using System.Windows.Media;

namespace Scanalyse.Desktop;

/// <summary>Fenêtre qui accueille le programme détaché de l'onglet Étiquette imprimante (deuxième écran).</summary>
public partial class ProgramWindow : Window
{
    public ProgramWindow()
    {
        InitializeComponent();
        WindowFrame.Attach(this);

        // Le contenu détaché suit la taille de l'interface (il quitte la page, et donc son échelle).
        UiScale.Follow(Host);
        MinWidth = Math.Min(MinWidth * Math.Min(1, UiScale.Value), SystemParameters.WorkArea.Width);
        MinHeight = Math.Min(MinHeight * Math.Min(1, UiScale.Value), SystemParameters.WorkArea.Height);
    }

    /// <summary>Titre affiché dans la barre de la fenêtre.</summary>
    public string Heading
    {
        get => TitleText.Text;
        set
        {
            TitleText.Text = value;
            Title = value + " — Scanalyse";
        }
    }

    /// <summary>Info-bulle du bouton de fermeture (par défaut : rattacher le programme).</summary>
    public string CloseTip
    {
        get => CloseButton.ToolTip as string ?? string.Empty;
        set => CloseButton.ToolTip = value;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Une fenêtre sans cadre agrandie déborde de l'écran : on compense par une marge.</summary>
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized ? "M2.5 0.5h7v7M0.5 2.5h7v7h-7z" : "M0.5 0.5h9v9h-9z");
    }
}
