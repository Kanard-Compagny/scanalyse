using System.Windows;
using Microsoft.Win32;

namespace Scanalyse.Desktop;

/// <summary>Choix du thème.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Applique le thème clair ou sombre à toute l'application (et à l'Atelier via l'événement <see cref="Changed"/>).
/// En mode « Système », suit le réglage clair/sombre de Windows, y compris s'il change pendant l'utilisation.
/// </summary>
public static class ThemeManager
{
    private static readonly Uri LightUri = new("pack://application:,,,/Scanalyse;component/Themes/Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkUri = new("pack://application:,,,/Scanalyse;component/Themes/Dark.xaml", UriKind.Absolute);
    private static bool _initialized;

    /// <summary>Thème choisi par l'utilisateur.</summary>
    public static AppTheme Mode { get; private set; } = AppTheme.System;

    /// <summary>Le thème réellement affiché est sombre.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Déclenché après chaque changement de thème affiché.</summary>
    public static event Action? Changed;

    /// <summary>À appeler une fois au démarrage.</summary>
    public static void Initialize(AppTheme mode)
    {
        if (!_initialized)
        {
            _initialized = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && Mode == AppTheme.System)
                {
                    Application.Current?.Dispatcher.InvokeAsync(() => Apply(AppTheme.System));
                }
            };
        }

        Apply(mode);
    }

    /// <summary>Applique un thème ; <paramref name="save"/> le mémorise pour les prochains lancements.</summary>
    public static void Apply(AppTheme mode, bool save = false)
    {
        Mode = mode;
        var dark = mode == AppTheme.Dark || (mode == AppTheme.System && WindowsUsesDarkTheme());
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var theme = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        if (dictionaries.Count > 0)
        {
            dictionaries[0] = theme;
        }
        else
        {
            dictionaries.Add(theme);
        }

        IsDark = dark;
        if (save)
        {
            var settings = AppSettings.Load();
            settings.Theme = mode;
            settings.Save();
        }

        Changed?.Invoke();
    }

    /// <summary>Libellé court du thème choisi.</summary>
    public static string Label(AppTheme mode) => mode switch
    {
        AppTheme.Light => "Clair",
        AppTheme.Dark => "Sombre",
        _ => "Système",
    };

    private static bool WindowsUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }
}
