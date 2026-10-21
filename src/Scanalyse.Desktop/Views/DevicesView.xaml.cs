using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Scanalyse.Core.Devices;
using Scanalyse.Core.Text;
using Scanalyse.Desktop.Scanners;

namespace Scanalyse.Desktop.Views;

/// <summary>État de la liaison avec la douchette, pour la barre d'outils et le journal.</summary>
/// <param name="Kind">Type de liaison active.</param>
/// <param name="Text">Texte court (« COM5 · 9600 8N1 »).</param>
/// <param name="Error">La dernière tentative a échoué ou la liaison est perdue.</param>
public sealed record ScannerStatus(ScanSourceKind Kind, string Text, bool Error = false);

/// <summary>Page Douchettes : détection, connexion en port série ou HID POS, moniteur des lectures.</summary>
public partial class DevicesView : UserControl
{
    private const int MaxMonitorItems = 200;

    private readonly ObservableCollection<MonitorItem> _monitor = [];
    private IScanSource? _source;
    private DeviceEntry? _connected;
    private bool _busy;

    public DevicesView()
    {
        InitializeComponent();
        MonitorList.ItemsSource = _monitor;
        LoadSerialPreferences(AppSettings.Load().Scanner.Serial);
    }

    /// <summary>Une lecture est arrivée (thread de l'interface).</summary>
    public event Action<ScanEvent>? Scanned;

    /// <summary>La liaison a changé (thread de l'interface).</summary>
    public event Action<ScannerStatus>? StatusChanged;

    /// <summary>Ouvrir une caméra en lecture directe (nom Windows de la caméra).</summary>
    public event Action<string>? CameraRequested;

    /// <summary>Message pour le journal (gravité, texte).</summary>
    public event Action<LogKind, string>? Log;

    /// <summary>Douchette sélectionnée dans la liste.</summary>
    private DeviceEntry? Selected =>
        DeviceList.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as DeviceEntry;

    /// <summary>Détecte les douchettes et reconnecte la dernière utilisée si elle est branchée.</summary>
    public async Task InitializeAsync()
    {
        var prefs = AppSettings.Load().Scanner;
        await RefreshAsync(prefs.DeviceId);
        if (prefs.Kind is ScanSourceKind.Serial or ScanSourceKind.Pos && Selected is { } entry && entry.Id == prefs.DeviceId)
        {
            await ConnectAsync(entry, silent: true);
        }
        else
        {
            SetStatus(new ScannerStatus(ScanSourceKind.Keyboard, "Douchette en mode clavier"));
        }
    }

    /// <summary>Ferme proprement la liaison (fermeture du programme).</summary>
    public async Task ShutdownAsync() => await CloseSourceAsync();

    // ------------------------------------------------------------------ Liste des douchettes

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RefreshAsync(Selected?.Id ?? _connected?.Id);

    private async Task RefreshAsync(string? keepId)
    {
        IReadOnlyList<DeviceEntry> devices;
        DeviceHint.Visibility = Visibility.Collapsed;
        StatusText.Text = _source is null ? "Recherche des douchettes…" : StatusText.Text;
        try
        {
            devices = await Task.Run(() => DeviceCatalog.FindAsync());
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            devices = [DeviceCatalog.Keyboard];
            Log?.Invoke(LogKind.Warning, "Recherche des douchettes incomplète : " + ex.Message);
        }

        DeviceList.Children.Clear();
        foreach (var d in devices)
        {
            var card = new RadioButton
            {
                Style = (Style)FindResource("ChoiceCard"),
                GroupName = "Device",
                Tag = d,
                Margin = new Thickness(0, 0, 0, 8),
                Content = DeviceContent(d),
            };
            card.Checked += Device_Checked;
            DeviceList.Children.Add(card);
        }

        var keep = DeviceList.Children.OfType<RadioButton>().FirstOrDefault(r => ((DeviceEntry)r.Tag).Id == keepId)
            ?? (RadioButton)DeviceList.Children[0];
        keep.IsChecked = true;
        DeviceHint.Visibility = devices.Count <= 1 ? Visibility.Visible : Visibility.Collapsed;
        if (_source is null)
        {
            StatusText.Text = "Douchette en mode clavier";
        }
    }

    private FrameworkElement DeviceContent(DeviceEntry d)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = d.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var detail = new TextBlock { Text = d.Detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        text.Children.Add(detail);
        grid.Children.Add(text);

        var badge = new Border { Style = (Style)FindResource("Pill"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        if (d.Vendor is { IsScannerBrand: true })
        {
            badge.SetResourceReference(Border.BackgroundProperty, "BrandSoft");
        }

        var badgeText = new TextBlock { Text = d.Badge, FontSize = 11, FontWeight = FontWeights.SemiBold };
        badgeText.SetResourceReference(TextBlock.ForegroundProperty, d.Vendor is { IsScannerBrand: true } ? "BrandText" : "Muted");
        badge.Child = badgeText;
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        return grid;
    }

    private void Device_Checked(object sender, RoutedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        var sel = Selected;
        SerialCard.Visibility = sel?.Kind == ScanSourceKind.Serial ? Visibility.Visible : Visibility.Collapsed;
        DisconnectButton.Visibility = _source is null ? Visibility.Collapsed : Visibility.Visible;
        ConnectButton.Content = sel?.Kind switch
        {
            ScanSourceKind.Keyboard => "Utiliser le mode clavier",
            ScanSourceKind.Camera => "Lire en direct",
            _ => _connected is not null && sel?.Id == _connected.Id ? "Reconnecter" : "Connecter",
        };
        ConnectButton.IsEnabled = !_busy && sel is not null;
    }

    // ------------------------------------------------------------------ Connexion

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { Kind: ScanSourceKind.Camera } camera)
        {
            CameraRequested?.Invoke(camera.Name);
        }
        else if (Selected is { } entry)
        {
            await ConnectAsync(entry, silent: false);
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        await CloseSourceAsync();
        Save(ScanSourceKind.Keyboard, string.Empty);
        Log?.Invoke(LogKind.Info, "Douchette déconnectée : retour au mode clavier.");
        SetStatus(new ScannerStatus(ScanSourceKind.Keyboard, "Douchette en mode clavier"));
    }

    private async Task ConnectAsync(DeviceEntry entry, bool silent)
    {
        _busy = true;
        UpdateButtons();
        try
        {
            await CloseSourceAsync();
            if (entry.Kind == ScanSourceKind.Keyboard)
            {
                Save(ScanSourceKind.Keyboard, string.Empty);
                SetStatus(new ScannerStatus(ScanSourceKind.Keyboard, "Douchette en mode clavier"));
                return;
            }

            var serial = ReadSerialSettings(entry.Id);
            IScanSource source = entry.Kind == ScanSourceKind.Serial
                ? new SerialScanSource(serial)
                : new PosScanSource(entry.Id, entry.Vendor?.Name ?? entry.Name);
            source.Scanned += e => Dispatcher.InvokeAsync(() => OnScanned(e));
            source.Faulted += m => Dispatcher.InvokeAsync(() => OnFaulted(m));

            SetStatus(new ScannerStatus(entry.Kind, $"Connexion à {entry.Badge}…"));
            await source.StartAsync();
            _source = source;
            _connected = entry;
            Save(entry.Kind, entry.Id, serial);
            SetStatus(new ScannerStatus(entry.Kind, source.Name));
            Log?.Invoke(LogKind.Ok, $"Connecté à {source.Name}. Scanne un code : la lecture arrive au banc d'essai.");
        }
        catch (ScannerException ex)
        {
            SetStatus(new ScannerStatus(entry.Kind, "Connexion impossible", Error: true));
            Log?.Invoke(silent ? LogKind.Warning : LogKind.Error, ex.Message);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private async Task CloseSourceAsync()
    {
        var source = _source;
        _source = null;
        _connected = null;
        if (source is not null)
        {
            await source.DisposeAsync();
        }

        UpdateButtons();
    }

    private void OnScanned(ScanEvent e)
    {
        var header = $"{e.At:HH:mm:ss} · {e.Source} · {e.Raw.Length} octet{(e.Raw.Length > 1 ? "s" : string.Empty)}"
            + (e.Symbology is null ? string.Empty : $" · symbologie annoncée : {e.Symbology}");
        _monitor.Insert(0, new MonitorItem(header, ControlCharacters.Visualize(e.Text), e.Hex));
        while (_monitor.Count > MaxMonitorItems)
        {
            _monitor.RemoveAt(_monitor.Count - 1);
        }

        MonitorEmpty.Visibility = Visibility.Collapsed;
        Scanned?.Invoke(e);
    }

    private async void OnFaulted(string message)
    {
        if (_source is null)
        {
            return;
        }

        await CloseSourceAsync();
        SetStatus(new ScannerStatus(ScanSourceKind.Keyboard, "Liaison perdue", Error: true));
        Log?.Invoke(LogKind.Error, message + " Rebranche-la, puis Actualiser et Connecter.");
    }

    private void SetStatus(ScannerStatus status)
    {
        StatusText.Text = status.Text;
        StatusDot.SetResourceReference(Shape.FillProperty,
            status.Error ? "BadFg" : status.Kind == ScanSourceKind.Keyboard ? "Muted" : "OkFg");
        StatusChanged?.Invoke(status);
    }

    private void ClearMonitor_Click(object sender, RoutedEventArgs e)
    {
        _monitor.Clear();
        MonitorEmpty.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------ Réglages série

    private SerialSettings ReadSerialSettings(string port) => new()
    {
        PortName = port,
        BaudRate = int.Parse(Chosen(BaudChips, "9600"), System.Globalization.CultureInfo.InvariantCulture),
        DataBits = Chosen(DataBitsChips, "8") == "7" ? 7 : 8,
        Parity = Enum.Parse<Parity>(Chosen(ParityChips, "None")),
        StopBits = Enum.Parse<StopBits>(Chosen(StopBitsChips, "One")),
        Handshake = Enum.Parse<Handshake>(Chosen(FlowChips, "None")),
        DtrEnable = Chosen(DtrChips, "True") == "True",
        Decoding = Enum.Parse<TextDecoding>(Chosen(EncodingChips, "Auto")),
        IdleGapMs = int.TryParse(IdleBox.Text, out var ms) ? Math.Clamp(ms, 10, 2000) : 80,
    };

    private void LoadSerialPreferences(SerialSettings s)
    {
        Choose(BaudChips, s.BaudRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Choose(DataBitsChips, s.DataBits == 7 ? "7" : "8");
        Choose(ParityChips, s.Parity.ToString());
        Choose(StopBitsChips, s.StopBits.ToString());
        Choose(FlowChips, s.Handshake.ToString());
        Choose(DtrChips, s.DtrEnable ? "True" : "False");
        Choose(EncodingChips, s.Decoding.ToString());
        IdleBox.Text = s.IdleGapMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Chosen(Panel group, string fallback) =>
        group.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? fallback;

    private static void Choose(Panel group, string tag)
    {
        if (group.Children.OfType<RadioButton>().FirstOrDefault(r => (r.Tag as string) == tag) is { } r)
        {
            r.IsChecked = true;
        }
    }

    private static void Save(ScanSourceKind kind, string deviceId, SerialSettings? serial = null)
    {
        var settings = AppSettings.Load();
        settings.Scanner.Kind = kind;
        settings.Scanner.DeviceId = deviceId;
        if (serial is not null)
        {
            settings.Scanner.Serial = serial;
        }

        settings.Save();
    }
}

/// <summary>Ligne du moniteur.</summary>
public sealed record MonitorItem(string Header, string Visual, string Hex);
