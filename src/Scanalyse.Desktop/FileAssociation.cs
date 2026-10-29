using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Scanalyse.Desktop;

/// <summary>
/// « Ouvrir avec Scanalyse » : inscrit Scanalyse dans le menu « Ouvrir avec » de l'Explorateur pour les programmes
/// d'étiquettes, les images et les PDF, et l'associe aux profils d'équipe. Uniquement pour la session Windows de
/// l'utilisateur (HKEY_CURRENT_USER), sans droits administrateur ; ne remplace jamais le programme par défaut d'un
/// type de fichier existant. Le chemin est remis à jour à chaque lancement (programme portable déplacé) et tout est
/// retiré par « Désinstaller ».
/// </summary>
internal static class FileAssociation
{
    private const string Classes = @"Software\Classes";
    private const string AppKey = Classes + @"\Applications\Scanalyse.exe";
    private const string ProfileProgId = "Scanalyse.ProfilEquipe";

    /// <summary>Extensions proposées dans « Ouvrir avec » : programmes d'imprimante, images et PDF.</summary>
    public static readonly string[] Extensions =
    [
        ".zpl", ".prn", ".epl", ".tspl", ".dpl", ".sbpl", ".lbl", ".txt",
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".pdf",
    ];

    /// <summary>Inscrit (ou remet à jour) Scanalyse pour l'utilisateur courant.</summary>
    public static void Register()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            return;
        }

        var command = $"\"{exe}\" \"%1\"";
        using (var app = Registry.CurrentUser.CreateSubKey(AppKey))
        {
            app.SetValue("FriendlyAppName", "Scanalyse");
            using (var icon = app.CreateSubKey("DefaultIcon"))
            {
                icon.SetValue(string.Empty, $"\"{exe}\",0");
            }

            using (var open = app.CreateSubKey(@"shell\open\command"))
            {
                open.SetValue(string.Empty, command);
            }

            using var types = app.CreateSubKey("SupportedTypes");
            foreach (var ext in Extensions)
            {
                types.SetValue(ext, string.Empty);
            }
        }

        // Chaque extension liste Scanalyse dans « Ouvrir avec », sans changer son programme par défaut.
        foreach (var ext in Extensions)
        {
            using var list = Registry.CurrentUser.CreateSubKey($@"{Classes}\{ext}\OpenWithList\Scanalyse.exe");
        }

        // Profil d'équipe : type propre à Scanalyse, ouvert directement par un double-clic.
        using (var progId = Registry.CurrentUser.CreateSubKey($@"{Classes}\{ProfileProgId}"))
        {
            progId.SetValue(string.Empty, "Profil d'équipe Scanalyse");
            using (var icon = progId.CreateSubKey("DefaultIcon"))
            {
                icon.SetValue(string.Empty, $"\"{exe}\",0");
            }

            using var open = progId.CreateSubKey(@"shell\open\command");
            open.SetValue(string.Empty, command);
        }

        using (var profile = Registry.CurrentUser.CreateSubKey($@"{Classes}\{TeamProfile.Extension}"))
        {
            profile.SetValue(string.Empty, ProfileProgId);
        }

        Notify();
    }

    /// <summary>Retire tout ce que <see cref="Register"/> a inscrit (n'efface jamais une clé d'un autre programme).</summary>
    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(AppKey, throwOnMissingSubKey: false);
            foreach (var ext in Extensions)
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}\{ext}\OpenWithList\Scanalyse.exe", throwOnMissingSubKey: false);
            }

            Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}\{ProfileProgId}", throwOnMissingSubKey: false);
            using (var profile = Registry.CurrentUser.OpenSubKey($@"{Classes}\{TeamProfile.Extension}"))
            {
                // Clé de l'extension retirée seulement si elle pointe encore vers Scanalyse.
                if (profile?.GetValue(string.Empty) as string != ProfileProgId)
                {
                    Notify();
                    return;
                }
            }

            Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}\{TeamProfile.Extension}", throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
        }

        Notify();
    }

    /// <summary>Prévient l'Explorateur que les associations ont changé (menus et icônes à jour sans redémarrer).</summary>
    private static void Notify() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
