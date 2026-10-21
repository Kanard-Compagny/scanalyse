using System.IO.Ports;
using Scanalyse.Core.Devices;
using Windows.Devices.Enumeration;
using Windows.Devices.PointOfService;
using Windows.Devices.SerialCommunication;

namespace Scanalyse.Desktop.Scanners;

/// <summary>Périphérique de lecture trouvé sur le PC.</summary>
/// <param name="Kind">Type de liaison.</param>
/// <param name="Id">Port (« COM5 ») ou identifiant Windows du scanner HID POS.</param>
/// <param name="Name">Nom donné par Windows.</param>
/// <param name="Vendor">Fabricant reconnu, si possible.</param>
public sealed record DeviceEntry(ScanSourceKind Kind, string Id, string Name, UsbVendor? Vendor)
{
    /// <summary>Ligne de détail affichée sous le nom.</summary>
    public string Detail => Kind switch
    {
        ScanSourceKind.Serial => Vendor is null ? $"Port série {Id}" : $"Port série {Id} · {Vendor.Name}",
        ScanSourceKind.Pos => Vendor is null ? "HID POS (Point de vente Windows)" : $"HID POS · {Vendor.Name}",
        _ => "La lecture arrive comme une frappe au clavier, dans le champ qui a le focus.",
    };

    /// <summary>Badge court.</summary>
    public string Badge => Kind switch
    {
        ScanSourceKind.Serial => Id,
        ScanSourceKind.Pos => "HID POS",
        _ => "Clavier",
    };
}

/// <summary>Recherche les douchettes reliées en port série ou en HID POS.</summary>
internal static class DeviceCatalog
{
    private const string PortNameProperty = "System.DeviceInterface.Serial.PortName";

    /// <summary>Au-delà, on renonce à la recherche Windows (pilotes lents, Bluetooth…) et on garde ce qu'on a.</summary>
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(4);

    /// <summary>Entrée « mode clavier », toujours disponible.</summary>
    public static DeviceEntry Keyboard { get; } = new(ScanSourceKind.Keyboard, "keyboard", "Mode clavier (émulation)", null);

    public static async Task<IReadOnlyList<DeviceEntry>> FindAsync()
    {
        var list = new List<DeviceEntry> { Keyboard };

        // Ports série : la liste de .NET est la référence, Windows donne le nom lisible et le fabricant.
        var names = new Dictionary<string, DeviceInformation>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var infos = await WithTimeout(DeviceInformation.FindAllAsync(SerialDevice.GetDeviceSelector(), [PortNameProperty]).AsTask());
            foreach (var info in infos ?? [])
            {
                if (info.Properties.TryGetValue(PortNameProperty, out var p) && p is string port)
                {
                    names[port] = info;
                }
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Noms lisibles indisponibles : on garde les numéros de port.
        }

        foreach (var port in SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(PortNumber))
        {
            names.TryGetValue(port, out var info);
            var name = info?.Name is { Length: > 0 } n && !n.Contains(port, StringComparison.OrdinalIgnoreCase) ? $"{n} ({port})" : info?.Name ?? port;
            list.Add(new DeviceEntry(ScanSourceKind.Serial, port, name, UsbVendors.Identify(info?.Id)));
        }

        // Scanners HID POS reconnus par Windows.
        try
        {
            foreach (var info in await WithTimeout(DeviceInformation.FindAllAsync(BarcodeScanner.GetDeviceSelector()).AsTask()) ?? [])
            {
                list.Add(new DeviceEntry(ScanSourceKind.Pos, info.Id, info.Name, UsbVendors.Identify(info.Id)));
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
        }

        return list;
    }

    private static async Task<IReadOnlyList<DeviceInformation>?> WithTimeout(Task<DeviceInformationCollection> search)
    {
        var done = await Task.WhenAny(search, Task.Delay(SearchTimeout));
        return done == search ? await search : null;
    }

    private static int PortNumber(string port) =>
        port.Length > 3 && int.TryParse(port.AsSpan(3), out var n) ? n : int.MaxValue;
}
