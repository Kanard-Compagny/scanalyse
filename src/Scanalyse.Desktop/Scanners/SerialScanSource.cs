using System.IO;
using System.IO.Ports;
using Scanalyse.Core.Devices;

namespace Scanalyse.Desktop.Scanners;

/// <summary>Réglages d'un port série.</summary>
public sealed class SerialSettings
{
    public string PortName { get; set; } = "COM1";

    public int BaudRate { get; set; } = 9600;

    public Parity Parity { get; set; } = Parity.None;

    public int DataBits { get; set; } = 8;

    public StopBits StopBits { get; set; } = StopBits.One;

    public Handshake Handshake { get; set; } = Handshake.None;

    /// <summary>Certains scanners n'émettent que si le PC signale qu'il est prêt (DTR).</summary>
    public bool DtrEnable { get; set; } = true;

    public TextDecoding Decoding { get; set; } = TextDecoding.Auto;

    /// <summary>Silence (ms) qui marque la fin d'une lecture.</summary>
    public int IdleGapMs { get; set; } = 80;

    /// <summary>Résumé « 9600 8N1 ».</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary =>
        $"{BaudRate} {DataBits}{Parity switch { Parity.Even => 'E', Parity.Odd => 'O', Parity.Mark => 'M', Parity.Space => 'S', _ => 'N' }}{(StopBits == StopBits.Two ? 2 : 1)}";
}

/// <summary>Douchette sur port série (RS-232 ou port COM virtuel USB).</summary>
internal sealed class SerialScanSource(SerialSettings settings) : IScanSource
{
    private readonly object _gate = new();
    private readonly FrameAssembler _frames = new(TimeSpan.FromMilliseconds(Math.Clamp(settings.IdleGapMs, 10, 2000)));
    private SerialPort? _port;
    private Timer? _timer;

    public string Name => $"{settings.PortName} · {settings.Summary}";

    public event Action<ScanEvent>? Scanned;

    public event Action<string>? Faulted;

    public Task StartAsync()
    {
        var port = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
        {
            Handshake = settings.Handshake,
            DtrEnable = settings.DtrEnable,
            RtsEnable = settings.Handshake is not (Handshake.RequestToSend or Handshake.RequestToSendXOnXOff),
            ReadBufferSize = 64 * 1024,
        };

        try
        {
            port.Open();
        }
        catch (UnauthorizedAccessException ex)
        {
            port.Dispose();
            throw new ScannerException($"{settings.PortName} est déjà ouvert par un autre programme (logiciel du scanner, terminal série, autre instance de Scanalyse…). Ferme-le puis réessaie.", ex);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            port.Dispose();
            throw new ScannerException($"Impossible d'ouvrir {settings.PortName} : {ex.Message}", ex);
        }

        port.DataReceived += OnDataReceived;
        port.ErrorReceived += (_, e) => Faulted?.Invoke($"Erreur de transmission sur {settings.PortName} ({e.EventType}) : vérifie la vitesse et la parité.");
        _port = port;
        _timer = new Timer(_ => Poll(), null, 20, 20);
        return Task.CompletedTask;
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var port = _port;
            if (port is null || !port.IsOpen)
            {
                return;
            }

            var n = port.BytesToRead;
            if (n <= 0)
            {
                return;
            }

            var buffer = new byte[n];
            var read = port.Read(buffer, 0, n);
            byte[]? frame;
            lock (_gate)
            {
                frame = _frames.Append(buffer.AsSpan(0, read), DateTimeOffset.UtcNow);
            }

            Emit(frame);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            Faulted?.Invoke($"{settings.PortName} ne répond plus (douchette débranchée ?).");
        }
    }

    private void Poll()
    {
        byte[]? frame;
        lock (_gate)
        {
            frame = _frames.Poll(DateTimeOffset.UtcNow);
        }

        Emit(frame);
    }

    private void Emit(byte[]? bytes)
    {
        if (bytes is { Length: > 0 })
        {
            Scanned?.Invoke(new ScanEvent(DateTime.Now, Name, bytes, RawData.Decode(bytes, settings.Decoding), null));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_timer is not null)
        {
            await _timer.DisposeAsync();
            _timer = null;
        }

        var port = _port;
        _port = null;
        if (port is not null)
        {
            port.DataReceived -= OnDataReceived;
            try
            {
                port.Close();
            }
            catch (IOException)
            {
                // Port déjà disparu (USB débranché) : rien à fermer.
            }

            port.Dispose();
        }
    }
}
