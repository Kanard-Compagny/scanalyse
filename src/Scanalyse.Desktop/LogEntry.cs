namespace Scanalyse.Desktop;

/// <summary>Nature d'une ligne du journal.</summary>
public enum LogKind
{
    Info,
    Ok,
    Warning,
    Error,
}

/// <summary>Ligne du journal des lectures.</summary>
public sealed record LogEntry(DateTime Time, LogKind Kind, string Tag, string Text)
{
    /// <summary>Heure affichée.</summary>
    public string TimeText => Time.ToString("HH:mm:ss");

    /// <summary>Ligne complète, pour la copie.</summary>
    public override string ToString() => $"{TimeText}  {Tag,-12} {Text}";
}
