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
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
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
    }
}
