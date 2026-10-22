using System.Threading;
using System.Windows;

namespace Scanalyse.Desktop;

/// <summary>
/// Point d'entrée : une seule instance de Scanalyse à la fois (le relais terminal et les douchettes ne se
/// partagent pas), puis le thème mémorisé avant d'ouvrir la fenêtre.
/// </summary>
public partial class App : Application
{
    private const string InstanceName = "Local\\Scanalyse-instance";
    private const string ActivateName = "Local\\Scanalyse-activate";

    private Mutex? _instance;
    private EventWaitHandle? _activate;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(initiallyOwned: true, InstanceName, out var first);
        if (!first && e.Args.Contains("--restart"))
        {
            // Redémarrage demandé (changement de langue) : on attend que l'ancienne fenêtre se ferme.
            try
            {
                first = _instance.WaitOne(TimeSpan.FromSeconds(15));
            }
            catch (AbandonedMutexException)
            {
                first = true;
            }
        }

        if (!first)
        {
            // Déjà ouvert : on remet la fenêtre existante au premier plan et on s'arrête là.
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ActivateName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }

            Shutdown();
            return;
        }

        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
        var listener = new Thread(() =>
        {
            try
            {
                while (_activate.WaitOne())
                {
                    Dispatcher.InvokeAsync(BringToFront);
                }
            }
            catch (ObjectDisposedException)
            {
                // Fermeture du programme.
            }
        })
        { IsBackground = true, Name = "Scanalyse - activation" };
        listener.Start();

        ThemeManager.Initialize(AppSettings.Load().Theme);
        Updater.CleanupOld();
        var settings = AppSettings.Load();
        if (!settings.LanguageChosen && !settings.WelcomeDone)
        {
            // Premier lancement : la langue d'abord, avant l'accueil et la visite guidée.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var picker = new LanguageWindow();
            picker.ShowDialog();
            settings.Language = picker.Choice;
            settings.LanguageChosen = true;
            settings.Save();
        }

        // La fermeture de la fenêtre principale arrête le programme, même si une autre fenêtre est restée ouverte.
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        Localization.Initialize(settings.Language);
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    /// <summary>Relance Scanalyse (changement de langue) : la nouvelle instance attend la fermeture de celle-ci.</summary>
    public static void Restart()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--restart") { UseShellExecute = false });
        if (Current.MainWindow is { } main)
        {
            main.Close();
        }
        else
        {
            Current.Shutdown();
        }
    }

    private void BringToFront()
    {
        if (MainWindow is not { } w)
        {
            return;
        }

        if (w.WindowState == WindowState.Minimized)
        {
            w.WindowState = WindowState.Normal;
        }

        w.Show();
        w.Activate();
        w.Topmost = true;
        w.Topmost = false;
        w.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activate?.Dispose();
        if (_instance is not null)
        {
            try
            {
                _instance.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _instance.Dispose();
        }

        base.OnExit(e);

        // Filet de sécurité : si un pilote de douchette ou le moteur web retient le processus après la fermeture,
        // il est arrêté au bout de 3 secondes, pour que Scanalyse.exe ne reste jamais verrouillé.
        new Thread(() =>
        {
            Thread.Sleep(3000);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        })
        { IsBackground = true, Name = "Scanalyse - arrêt" }.Start();
    }
}
