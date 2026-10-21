using Scanalyse.Core.Devices;

namespace Scanalyse.Desktop.Scanners;

/// <summary>Façon dont la douchette est reliée au PC.</summary>
public enum ScanSourceKind
{
    /// <summary>Émulation clavier : la lecture arrive comme une frappe au clavier (réglage d'usine).</summary>
    Keyboard,

    /// <summary>Port série, réel ou virtuel (USB CDC / USB COM) : octets bruts.</summary>
    Serial,

    /// <summary>HID POS (USB HID Point of Sale) via Windows : octets bruts et symbologie annoncée.</summary>
    Pos,
}

/// <summary>Une lecture reçue d'une douchette connectée.</summary>
/// <param name="At">Heure de réception.</param>
/// <param name="Source">Nom de la source (« COM5 · 9600 bauds »…).</param>
/// <param name="Raw">Octets exactement tels que reçus.</param>
/// <param name="Text">Texte transmis au banc d'essai.</param>
/// <param name="Symbology">Symbologie annoncée par le scanner (HID POS uniquement).</param>
public sealed record ScanEvent(DateTime At, string Source, byte[] Raw, string Text, string? Symbology)
{
    /// <summary>Octets en hexadécimal.</summary>
    public string Hex => RawData.ToHex(Raw);
}

/// <summary>Douchette connectée en direct (hors émulation clavier).</summary>
public interface IScanSource : IAsyncDisposable
{
    /// <summary>Nom affiché.</summary>
    string Name { get; }

    /// <summary>Une lecture est arrivée (thread quelconque).</summary>
    event Action<ScanEvent>? Scanned;

    /// <summary>La connexion est perdue (douchette débranchée…) : message pour l'utilisateur.</summary>
    event Action<string>? Faulted;

    /// <summary>Ouvre la connexion. Lève <see cref="ScannerException"/> avec un message clair en cas d'échec.</summary>
    Task StartAsync();
}

/// <summary>Échec de connexion, avec un message prêt à afficher.</summary>
public sealed class ScannerException(string message, Exception? inner = null) : Exception(message, inner);
