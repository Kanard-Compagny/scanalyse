using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;

namespace Scanalyse.Desktop;

/// <summary>
/// Point d'entrée : filet de sécurité contre les erreurs imprévues, une seule instance de Scanalyse à la fois (le relais
/// terminal et les douchettes ne se partagent pas), puis le thème mémorisé avant d'ouvrir la fenêtre. Les fichiers
/// passés au programme (« Ouvrir avec », glisser sur Scanalyse.exe) s'ouvrent au bon endroit, y compris dans la
/// fenêtre déjà ouverte.
/// </summary>
public partial class App : Application
{
    private const string InstanceName = "Local\\Scanalyse-instance";
    private const string ActivateName = "Local\\Scanalyse-activate";

    private Mutex? _instance;
    private EventWaitHandle? _activate;
    private CancellationTokenSource? _pipeStop;
    private SplashScreen? _splash;
    private readonly System.Diagnostics.Stopwatch _splashClock = new();

    /// <summary>Durée minimale d'affichage de l'écran de démarrage, pour qu'il ne fasse pas qu'un flash.</summary>
    private static readonly TimeSpan SplashMinimum = TimeSpan.FromMilliseconds(1500);

    /// <summary>Canal par lequel une seconde instance transmet ses fichiers (propre à la session Windows et à l'utilisateur).</summary>
    private static string PipeName => $"Scanalyse-ouvrir-{System.Diagnostics.Process.GetCurrentProcess().SessionId}-{Environment.UserName}";

    protected override void OnStartup(StartupEventArgs e)
    {
        CrashReporter.Install(this);
        var files = e.Args.Where(a => !a.StartsWith("--", StringComparison.Ordinal) && File.Exists(a)).Select(Path.GetFullPath).ToArray();
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
            // Déjà ouvert : les fichiers partent à la fenêtre existante, remise au premier plan ; on s'arrête là.
            SendFiles(files);
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

        ShowSplash();
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
        StartFileListener();

        ThemeManager.Initialize(AppSettings.Load().Theme);
        Updater.CleanupOld();
        var settings = AppSettings.Load();
        if (!settings.LanguageChosen && !settings.WelcomeDone)
        {
            // Premier lancement : la langue d'abord, avant l'accueil et la visite guidée.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _splash?.Close(TimeSpan.Zero);
            _splash = null;
            var picker = new LanguageWindow();
            picker.ShowDialog();
            settings.Language = picker.Choice;
            settings.LanguageChosen = true;
            settings.Save();
            ShowSplash();
        }

        // La fermeture de la fenêtre principale arrête le programme, même si une autre fenêtre est restée ouverte.
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        Localization.Initialize(settings.Language);
        if (settings.OpenWith)
        {
            // Programme portable : le chemin inscrit dans « Ouvrir avec » suit l'emplacement actuel de Scanalyse.exe.
            try
            {
                FileAssociation.Register();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
            }
        }

        base.OnStartup(e);
        var main = new MainWindow();
        MainWindow = main;
        main.StartupFinished += CloseSplash;
        main.Show();
        if (files.Length > 0)
        {
            _ = main.OpenFilesAsync(files);
        }
    }

    /// <summary>Seconde instance : transmet ses fichiers à la première par un canal nommé (une ligne par fichier).</summary>
    private static void SendFiles(string[] files)
    {
        if (files.Length == 0)
        {
            return;
        }

        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(3000);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            foreach (var f in files)
            {
                writer.WriteLine(f);
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Première instance : reçoit les fichiers des instances suivantes et les ouvre dans la fenêtre.</summary>
    private void StartFileListener()
    {
        _pipeStop = new CancellationTokenSource();
        var token = _pipeStop.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    var received = new List<string>();
                    while (await reader.ReadLineAsync(token) is { } line)
                    {
                        if (File.Exists(line))
                        {
                            received.Add(line);
                        }
                    }

                    if (received.Count > 0)
                    {
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            BringToFront();
                            if (MainWindow is MainWindow w)
                            {
                                await w.OpenFilesAsync(received);
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    await Task.Delay(500, CancellationToken.None);
                }
            }
        }, token);
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
        _pipeStop?.Cancel();
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

    /// <summary>
    /// Écran de démarrage piloté à la main : il reste au premier plan tant que la fenêtre principale et l'Atelier ne sont
    /// pas prêts (l'affichage automatique de WPF se fermait dès la première image, bien avant la fin du chargement).
    /// </summary>
    private void ShowSplash()
    {
        try
        {
            _splash = new SplashScreen("Assets/splash.png");
            _splash.Show(autoClose: false, topMost: true);
            _splashClock.Restart();
        }
        catch (IOException)
        {
            _splash = null;
        }
    }

    private async void CloseSplash()
    {
        var splash = _splash;
        _splash = null;
        if (splash is null)
        {
            return;
        }

        var left = SplashMinimum - _splashClock.Elapsed;
        if (left > TimeSpan.Zero)
        {
            await Task.Delay(left);
        }

        splash.Close(TimeSpan.FromMilliseconds(300));
        MainWindow?.Activate();
    }
}
