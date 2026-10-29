using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Scanalyse.Desktop;

/// <summary>
/// Filet de sécurité : une erreur imprévue ne ferme plus Scanalyse sans un mot. Le rapport (versions, système, erreur)
/// est enregistré dans le dossier « Plantages » des réglages, et une fenêtre propose de le copier ou de le signaler sur
/// GitHub. Erreur de l'interface : on peut continuer. Erreur fatale (autre fil d'exécution) : le programme se ferme.
/// Le rapport ne contient ni image ni donnée lue ; le dossier de l'utilisateur y est remplacé par %USERPROFILE%.
/// </summary>
internal static class CrashReporter
{
    private const string IssuesUrl = "https://github.com/Kanard-Compagny/scanalyse/issues/new";
    private const int KeptReports = 20;
    private static int _showing;

    /// <summary>Dossier des rapports d'erreur.</summary>
    public static string Folder => Path.Combine(AppSettings.DataFolder, "Plantages");

    /// <summary>Branche les trois sources d'erreurs non gérées (interface, autres fils, tâches oubliées).</summary>
    public static void Install(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.DispatcherUnhandledException += (_, e) => e.Handled = Report(e.Exception, recoverable: true);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString()), recoverable: false);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // Tâche en arrière-plan oubliée : pas de fenêtre, le rapport suffit.
            Write(e.Exception, "tâche en arrière-plan");
            e.SetObserved();
        };
    }

    /// <summary>Enregistre et affiche le rapport. Renvoie <c>true</c> si l'erreur est traitée (le programme continue ou se ferme proprement).</summary>
    private static bool Report(Exception exception, bool recoverable)
    {
        var (text, path) = Write(exception, recoverable ? "interface" : "fatale");
        if (Interlocked.Exchange(ref _showing, 1) == 1)
        {
            // Une fenêtre d'erreur est déjà ouverte : on ne les empile pas.
            return true;
        }

        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                return false;
            }

            var keepRunning = false;
            void Show() => keepRunning = new CrashWindow(text, path, recoverable).ShowDialog() == true;
            if (dispatcher.CheckAccess())
            {
                Show();
            }
            else
            {
                dispatcher.Invoke(Show);
            }

            if (!keepRunning)
            {
                Application.Current?.Shutdown();
            }

            return true;
        }
        catch (Exception)
        {
            // La fenêtre d'erreur elle-même a échoué : on laisse Windows faire, le rapport est déjà enregistré.
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _showing, 0);
        }
    }

    /// <summary>Compose le rapport et l'enregistre (les 20 derniers sont gardés).</summary>
    private static (string Text, string? Path) Write(Exception exception, string kind)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Scanalyse - rapport d'erreur");
        sb.AppendLine($"Date         {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Erreur       {kind}");
        try
        {
            sb.AppendLine(Views.SettingsView.Diagnostic());
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Diagnostic indisponible : {ex.GetType().Name}");
        }

        sb.AppendLine();
        sb.AppendLine(exception.ToString());
        var text = Anonymize(sb.ToString());

        string? path = null;
        try
        {
            Directory.CreateDirectory(Folder);
            path = Path.Combine(Folder, $"erreur-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            File.WriteAllText(path, text, Encoding.UTF8);
            foreach (var old in new DirectoryInfo(Folder).GetFiles("erreur-*.txt").OrderByDescending(f => f.Name).Skip(KeptReports))
            {
                old.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return (text, path);
    }

    /// <summary>Le dossier personnel (qui contient le nom de l'utilisateur) devient %USERPROFILE%.</summary>
    private static string Anonymize(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length > 3 ? text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase) : text;
    }

    /// <summary>Nouveau ticket GitHub (formulaire « Un problème ») pré-rempli avec le rapport.</summary>
    public static string IssueUrl(string report)
    {
        var first = report.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains("Exception", StringComparison.Ordinal) || l.Contains("Error", StringComparison.Ordinal)) ?? "Erreur imprévue";
        var title = "Plantage : " + (first.Length > 90 ? first[..90] + "…" : first);

        // Les adresses trop longues sont refusées par GitHub : le rapport est tronqué (il reste complet dans le fichier).
        var body = report.Length > 5500 ? report[..5500] + "\n[…] (rapport complet dans le dossier Plantages)" : report;
        return $"{IssuesUrl}?template=1-probleme.yml&title={Uri.EscapeDataString(title)}&diagnostic={Uri.EscapeDataString(body)}";
    }

    /// <summary>Fenêtre d'erreur, construite sans XAML : elle doit s'ouvrir même si une ressource du programme est en cause.</summary>
    private sealed class CrashWindow : Window
    {
        public CrashWindow(string report, string? path, bool recoverable)
        {
            Title = Localization.T("Scanalyse - erreur imprévue");
            Width = 660;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 760;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            ShowInTaskbar = true;
            SetBrush("Bg", BackgroundProperty, Brushes.White);
            SetBrush("Ink", ForegroundProperty, Brushes.Black);
            FontFamily = TryFindResource("UiFont") as FontFamily ?? new FontFamily("Segoe UI");
            FontSize = 14;

            var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
            root.Children.Add(new TextBlock
            {
                Text = Localization.T("Scanalyse a rencontré une erreur imprévue"),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
            });
            root.Children.Add(Paragraph(recoverable
                ? Localization.T("Tu peux continuer, mais enregistre ton travail : le programme peut être instable. Pour éviter que ça se reproduise, envoie le rapport : il aide à corriger le problème.")
                : Localization.T("Scanalyse doit se fermer. Pour éviter que ça se reproduise, envoie le rapport : il aide à corriger le problème."), 10));
            root.Children.Add(Paragraph(Localization.T("Le rapport ne contient ni image ni donnée lue, seulement les versions, le système et l'erreur. Relis-le avant de l'envoyer."), 6, muted: true));

            var details = new TextBox
            {
                Text = report,
                IsReadOnly = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 11.5,
                Height = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                BorderThickness = new Thickness(1),
            };
            details.SetResourceReference(BackgroundProperty, "Surface2");
            details.SetResourceReference(ForegroundProperty, "Ink");
            details.SetResourceReference(BorderBrushProperty, "Line");
            root.Children.Add(new Expander { Header = Localization.T("Détails du rapport"), Content = details, Margin = new Thickness(0, 12, 0, 0) });

            var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            var copy = MakeButton(Localization.T("Copier le rapport"), "BtnSecondary");
            copy.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(report);
                    copy.Content = Localization.T("Rapport copié");
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                }
            };
            var issue = MakeButton(Localization.T("Signaler sur GitHub"), "BtnSecondary");
            issue.ToolTip = Localization.T("Ouvre un ticket déjà rempli avec le rapport (compte GitHub nécessaire)");
            issue.Click += (_, _) => Open(IssueUrl(report));
            actions.Children.Add(copy);
            actions.Children.Add(issue);
            if (path is not null)
            {
                var folder = MakeButton(Localization.T("Ouvrir le dossier des rapports"), "BtnGhost");
                folder.Click += (_, _) => Open(Folder);
                actions.Children.Add(folder);
            }

            root.Children.Add(actions);

            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            if (recoverable)
            {
                var keep = MakeButton(Localization.T("Continuer"), "BtnSecondary");
                keep.Click += (_, _) => DialogResult = true;
                bottom.Children.Add(keep);
            }

            var close = MakeButton(Localization.T("Fermer Scanalyse"), "BtnAction");
            close.Click += (_, _) => DialogResult = false;
            close.IsDefault = true;
            bottom.Children.Add(close);
            root.Children.Add(bottom);
            Content = root;
        }

        private void SetBrush(string key, DependencyProperty property, Brush fallback)
        {
            if (TryFindResource(key) is Brush)
            {
                SetResourceReference(property, key);
            }
            else
            {
                SetValue(property, fallback);
            }
        }

        private TextBlock Paragraph(string text, double top, bool muted = false)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0), FontSize = muted ? 12.5 : 14 };
            if (muted && TryFindResource("Muted") is Brush)
            {
                t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            }

            return t;
        }

        private Button MakeButton(string text, string style)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), MinWidth = 120 };
            if (TryFindResource(style) is Style s)
            {
                b.Style = s;
            }
            else
            {
                b.Padding = new Thickness(12, 6, 12, 6);
            }

            return b;
        }

        private static void Open(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
        }
    }
}
