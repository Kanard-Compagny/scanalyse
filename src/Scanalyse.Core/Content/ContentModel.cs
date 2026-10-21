using Scanalyse.Core.Gs1;
using Scanalyse.Core.Symbologies;

namespace Scanalyse.Core.Content;

/// <summary>Gravité d'une remarque.</summary>
public enum Severity
{
    Ok,
    Info,
    Warning,
    Error,
}

/// <summary>Remarque à afficher à l'utilisateur.</summary>
public sealed record Alert(Severity Severity, string Message);

/// <summary>Information extraite du contenu.</summary>
/// <param name="Label">Libellé.</param>
/// <param name="Value">Valeur.</param>
/// <param name="Sensitive">Donnée sensible (mot de passe, clé secrète) à masquer par défaut.</param>
/// <param name="Hint">Précision secondaire.</param>
public sealed record Field(string Label, string Value, bool Sensitive = false, string? Hint = null);

/// <summary>Nature du contenu d'un code.</summary>
public enum ContentKind
{
    Text,
    Number,
    WebLink,
    Gs1DigitalLink,
    Gs1,
    RetailGtin,
    Book,
    Periodical,
    Wifi,
    Contact,
    Email,
    Phone,
    Sms,
    Geo,
    CalendarEvent,
    SepaPayment,
    SwissQrBill,
    TwoDDoc,
    EuHealthCertificate,
    OneTimePassword,
    CryptoPayment,
    Json,
    AppLink,
    ZebraStageNow,
}

/// <summary>Données lues dans un code, telles que fournies par le décodeur ou le scanner.</summary>
/// <param name="Text">Texte décodé.</param>
/// <param name="Bytes">Octets bruts, si disponibles.</param>
/// <param name="Symbology">Symbologie, si connue.</param>
/// <param name="SymbologyIdentifier">Identifiant AIM (« ]d2 »…), si connu.</param>
public sealed record ScanInput(
    string Text,
    byte[]? Bytes = null,
    Symbology Symbology = Symbology.Unknown,
    string? SymbologyIdentifier = null)
{
    /// <summary>Octets bruts, ou à défaut le texte encodé en Latin-1.</summary>
    public byte[] RawBytes => Bytes ?? System.Text.Encoding.Latin1.GetBytes(Text);
}

/// <summary>Résultat de l'interprétation d'un contenu.</summary>
public sealed class ContentAnalysis
{
    public ContentAnalysis(ContentKind kind, string title)
    {
        Kind = kind;
        Title = title;
    }

    /// <summary>Nature du contenu.</summary>
    public ContentKind Kind { get; internal set; }

    /// <summary>Titre lisible (« Réseau Wi-Fi »…).</summary>
    public string Title { get; internal set; }

    /// <summary>Informations extraites, dans l'ordre d'affichage.</summary>
    public List<Field> Fields { get; } = [];

    /// <summary>Remarques et alertes.</summary>
    public List<Alert> Alerts { get; } = [];

    /// <summary>Données GS1 décodées, le cas échéant.</summary>
    public Gs1Message? Gs1 { get; internal set; }

    /// <summary>Lien ouvrable (page web, carte…), le cas échéant. Il n'est jamais ouvert par l'analyse.</summary>
    public Uri? Link { get; internal set; }

    /// <summary>Profil Zebra StageNow décodé, le cas échéant.</summary>
    public StageNowProfile? StageNow { get; internal set; }

    internal ContentAnalysis Add(string label, string value, bool sensitive = false, string? hint = null)
    {
        Fields.Add(new Field(label, value, sensitive, hint));
        return this;
    }

    internal ContentAnalysis Warn(Severity severity, string message)
    {
        if (!Alerts.Any(a => a.Message == message))
        {
            Alerts.Add(new Alert(severity, message));
        }

        return this;
    }
}
