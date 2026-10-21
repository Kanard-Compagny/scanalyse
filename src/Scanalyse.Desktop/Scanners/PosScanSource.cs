using Scanalyse.Core.Devices;
using Windows.Devices.PointOfService;
using Windows.Storage.Streams;

namespace Scanalyse.Desktop.Scanners;

/// <summary>
/// Douchette en mode HID POS, pilotée par l'API Point de vente de Windows : on reçoit les données
/// décodées, les octets bruts et la symbologie que le scanner annonce.
/// </summary>
internal sealed class PosScanSource(string deviceId, string displayName) : IScanSource
{
    private BarcodeScanner? _scanner;
    private ClaimedBarcodeScanner? _claimed;

    public string Name => $"{displayName} · HID POS";

    public event Action<ScanEvent>? Scanned;

    public event Action<string>? Faulted;

    public async Task StartAsync()
    {
        try
        {
            _scanner = await WithTimeout(BarcodeScanner.FromIdAsync(deviceId).AsTask())
                ?? throw new ScannerException($"{displayName} n'est plus disponible ou ne répond pas : rebranche-la puis actualise la liste.");
            _claimed = await WithTimeout(_scanner.ClaimScannerAsync().AsTask())
                ?? throw new ScannerException($"{displayName} est déjà utilisée par une autre application (logiciel de caisse, utilitaire du fabricant…) ou ne répond pas.");

            // Une autre application demande la douchette : on la garde tant que Scanalyse est connecté.
            _claimed.ReleaseDeviceRequested += (_, claimed) => claimed.RetainDevice();
            _claimed.DataReceived += OnDataReceived;
            _claimed.IsDecodeDataEnabled = true;
            await _claimed.EnableAsync();
            _scanner.StatusUpdated += (_, e) =>
            {
                if (e.Status is BarcodeScannerStatus.Off or BarcodeScannerStatus.Offline or BarcodeScannerStatus.OffOrOffline)
                {
                    Faulted?.Invoke($"{displayName} est hors ligne (débranchée ou éteinte).");
                }
            };
        }
        catch (ScannerException)
        {
            await DisposeAsync();
            throw;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            await DisposeAsync();
            throw new ScannerException($"Connexion HID POS impossible à {displayName} : {ex.Message}", ex);
        }
    }

    /// <summary>Un pilote qui ne répond pas ne doit jamais figer Scanalyse.</summary>
    private static async Task<T?> WithTimeout<T>(Task<T> task)
        where T : class
    {
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(8)));
        return done == task ? await task : null;
    }

    private void OnDataReceived(ClaimedBarcodeScanner sender, BarcodeScannerDataReceivedEventArgs args)
    {
        var report = args.Report;
        var raw = ToBytes(report.ScanData);
        var label = ToBytes(report.ScanDataLabel);
        var symbology = BarcodeSymbologies.GetName(report.ScanDataType);

        // Les applications HID POS utilisent les données décodées (« label ») ; les octets bruts peuvent
        // contenir en plus un en-tête propre au fabricant.
        var text = RawData.Decode(label.Length > 0 ? label : raw);
        Scanned?.Invoke(new ScanEvent(DateTime.Now, Name, raw.Length > 0 ? raw : label, text,
            string.IsNullOrEmpty(symbology) || symbology == "Unknown" ? null : symbology));
    }

    private static byte[] ToBytes(IBuffer? buffer)
    {
        if (buffer is null || buffer.Length == 0)
        {
            return [];
        }

        using var reader = DataReader.FromBuffer(buffer);
        var bytes = new byte[buffer.Length];
        reader.ReadBytes(bytes);
        return bytes;
    }

    public ValueTask DisposeAsync()
    {
        if (_claimed is not null)
        {
            _claimed.DataReceived -= OnDataReceived;
            _claimed.Dispose();
            _claimed = null;
        }

        _scanner?.Dispose();
        _scanner = null;
        return ValueTask.CompletedTask;
    }
}
