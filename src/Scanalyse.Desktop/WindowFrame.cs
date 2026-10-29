using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Scanalyse.Desktop;

/// <summary>
/// Cadre des fenêtres Scanalyse (barre de titre dessinée par le programme) : ombre portée de Windows, coins arrondis et
/// liseré gris sous Windows 11, liseré dessiné par WPF sous Windows 10. La fenêtre se détache ainsi des programmes ouverts
/// derrière elle, comme les fenêtres de Windows.
/// </summary>
internal static class WindowFrame
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;

    private static readonly List<WeakReference<Window>> Windows = [];

    /// <summary>Windows 11 (build 22000 ou plus) : coins arrondis et couleur du liseré gérés par le système.</summary>
    private static bool Windows11 => Environment.OSVersion.Version.Build >= 22000;

    static WindowFrame()
    {
        ThemeManager.Changed += () =>
        {
            foreach (var r in Windows.ToList())
            {
                if (r.TryGetTarget(out var w))
                {
                    ApplyBorderColor(w);
                }
                else
                {
                    Windows.Remove(r);
                }
            }
        };
    }

    /// <summary>À appeler dans le constructeur de la fenêtre, après InitializeComponent.</summary>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Windows.Add(new WeakReference<Window>(window));
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            // Ombre portée : un point de cadre DWM sous le contenu suffit à la faire revenir (GlassFrameThickness 0 la supprime).
            var margins = new Margins { Bottom = 1 };
            _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
            if (Windows11)
            {
                var round = DwmwcpRound;
                _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
            }

            ApplyBorderColor(window);
            if (window.ShowInTaskbar)
            {
                SetTaskbarIcon(hwnd);
            }
        };
        window.StateChanged += (_, _) => ApplyWpfBorder(window);
        window.Loaded += (_, _) => ApplyWpfBorder(window);
    }

    /// <summary>Liseré gris, de la teinte des barres de défilement du thème (discret en clair comme en sombre).</summary>
    private static void ApplyBorderColor(Window window)
    {
        ApplyWpfBorder(window);
        if (!Windows11)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || Application.Current?.TryFindResource("ScrollThumbColor") is not Color c)
        {
            return;
        }

        var colorRef = c.R | (c.G << 8) | (c.B << 16);
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref colorRef, sizeof(int));
    }

    /// <summary>Windows 10 : liseré WPF autour du contenu (aucun en plein écran) ; Windows 11 : le système le dessine.</summary>
    private static void ApplyWpfBorder(Window window)
    {
        if (window.FindName("RootBorder") is not Border root)
        {
            return;
        }

        if (Windows11 || window.WindowState == WindowState.Maximized)
        {
            root.BorderThickness = new Thickness(0);
            return;
        }

        root.SetResourceReference(Border.BorderBrushProperty, "ScrollThumb");
        root.BorderThickness = new Thickness(1);
    }

    /// <summary>
    /// Icône de la barre des tâches tirée de Scanalyse.exe lui-même (grande et petite tailles) : la barre des tâches ne reste
    /// jamais sans logo, même quand l'icône de la fenêtre (ressource WPF) n'est pas reprise par Windows.
    /// </summary>
    private static void SetTaskbarIcon(IntPtr hwnd)
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            return;
        }

        if (ExtractIconEx(exe, 0, out var large, out var small, 1) > 0)
        {
            if (large != IntPtr.Zero)
            {
                SendMessage(hwnd, WmSetIcon, IconBig, large);
            }

            if (small != IntPtr.Zero)
            {
                SendMessage(hwnd, WmSetIcon, IconSmall, small);
            }
        }
    }

    private const int WmSetIcon = 0x0080;
    private static readonly IntPtr IconSmall = IntPtr.Zero;
    private static readonly IntPtr IconBig = new(1);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
