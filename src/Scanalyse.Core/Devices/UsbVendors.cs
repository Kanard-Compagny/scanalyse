using System.Globalization;
using System.Text.RegularExpressions;

namespace Scanalyse.Core.Devices;

/// <summary>Fabricant reconnu à partir de l'identifiant USB (VID) d'un périphérique.</summary>
/// <param name="VendorId">Identifiant USB du fabricant.</param>
/// <param name="Name">Nom affiché.</param>
/// <param name="IsScannerBrand">Marque de scanners (et non simple adaptateur USB-série).</param>
public sealed record UsbVendor(int VendorId, string Name, bool IsScannerBrand);

/// <summary>Reconnaît les fabricants de scanners et d'adaptateurs USB-série courants.</summary>
public static partial class UsbVendors
{
    private static readonly Dictionary<int, UsbVendor> Known = new UsbVendor[]
    {
        new(0x05E0, "Zebra (Symbol)", true),
        new(0x0C2E, "Honeywell (Metrologic)", true),
        new(0x0536, "Honeywell (Hand Held Products)", true),
        new(0x05F9, "Datalogic", true),
        new(0x080C, "Datalogic", true),
        new(0x1EAB, "Newland", true),
        new(0x065A, "Opticon", true),
        new(0x0403, "Adaptateur USB-série FTDI", false),
        new(0x067B, "Adaptateur USB-série Prolific", false),
        new(0x10C4, "Adaptateur USB-série Silicon Labs", false),
        new(0x1A86, "Adaptateur USB-série CH340", false),
    }.ToDictionary(v => v.VendorId);

    /// <summary>Extrait le VID d'un identifiant de périphérique Windows (« USB\VID_05E0&amp;PID_1701… »), s'il y en a un.</summary>
    public static int? ParseVendorId(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return null;
        }

        var m = VidPattern().Match(deviceId);
        return m.Success ? int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Fabricant reconnu pour cet identifiant de périphérique, ou <c>null</c>.</summary>
    public static UsbVendor? Identify(string? deviceId) =>
        ParseVendorId(deviceId) is { } vid && Known.TryGetValue(vid, out var v) ? v : null;

    [GeneratedRegex(@"VID_([0-9A-Fa-f]{4})", RegexOptions.CultureInvariant)]
    private static partial Regex VidPattern();
}
