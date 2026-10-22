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

/// <summary>Page Douchettes : connexion des douchettes (série, HID POS), des terminaux Android (relais) et des caméras.</summary>
public partial class DevicesView : UserControl
{
    private readonly Relay.RelayServer _relay = new();
    private readonly Dictionary<string, Relay.AdbDevice> _openedUsb = [];
    private ScannerStatus _deviceStatus = new(ScanSourceKind.Keyboard, "Douchette en mode clavier");
    private string? _adb;
    private IScanSource? _source;
    private DeviceEntry? _connected;
    private bool _busy;

    public DevicesView()
    {
        InitializeComponent();
        LoadSerialPreferences(AppSettings.Load().Scanner.Serial);
    }

    /// <summary>Dessine un code (texte, type de l'Atelier) : sert au QR de l'adresse Wi-Fi du relais.</summary>
    public Func<string, string, Task<(byte[]? Png, string? Error)>>? Renderer { get; set; }

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
        _relay.Scanned += e => Dispatcher.InvokeAsync(() => OnScanned(e));
        _relay.Log += (kind, text) => Dispatcher.InvokeAsync(() => Log?.Invoke(kind, text));
        _relay.SessionsChanged += () => Dispatcher.InvokeAsync(OnSessionsChanged);
        var settings = AppSettings.Load();
        WifiCheck.IsChecked = settings.RelayWifi;
        await StartRelayAsync(settings.RelayWifi);
        _ = RefreshTerminalsAsync();

        var prefs = settings.Scanner;
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
    public async Task ShutdownAsync()
    {
        await CloseSourceAsync();
        await _relay.DisposeAsync();
    }

    // ------------------------------------------------------------------ Relais terminal Android

    private async Task StartRelayAsync(bool wifi)
    {
        var error = await _relay.StartAsync(wifi);
        if (error is not null)
        {
            Log?.Invoke(LogKind.Error, error);
        }

        var ips = Relay.RelayServer.LanAddresses();
        RawHint.Text = $"dans DataWedge, sortie IP en TCP vers 127.0.0.1 port {Relay.RelayServer.RawPort} pour un terminal relié en USB (après « Ouvrir le relais »)"
            + (wifi && ips.Count > 0 ? $", ou vers {ips[0]} port {Relay.RelayServer.RawPort} en Wi-Fi." : ".");
        WifiPanel.Visibility = wifi ? Visibility.Visible : Visibility.Collapsed;
        if (wifi)
        {
            if (ips.Count == 0)
            {
                WifiUrl.Text = "Aucun réseau local détecté sur ce PC.";
                WifiQr.Source = null;
                return;
            }

            var url = $"http://{ips[0]}:{Relay.RelayServer.HttpPort}/relais";
            WifiUrl.Text = url + (ips.Count > 1 ? $"\n(autres adresses : {string.Join(", ", ips.Skip(1))})" : string.Empty);
            await ShowQrAsync(url);
        }
    }

    private async Task ShowQrAsync(string url)
    {
        if (Renderer is null)
        {
            return;
        }

        // L'Atelier peut ne pas être encore prêt au démarrage : quelques essais.
        for (var i = 0; i < 10; i++)
        {
            var (png, _) = await Renderer(url, "qrplain");
            if (png is { Length: > 0 })
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                using (var ms = new System.IO.MemoryStream(png))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }

                bmp.Freeze();
                WifiQr.Source = bmp;
                return;
            }

            await Task.Delay(1500);
        }
    }

    private async void Wifi_Click(object sender, RoutedEventArgs e)
    {
        var wifi = WifiCheck.IsChecked == true;
        var settings = AppSettings.Load();
        settings.RelayWifi = wifi;
        settings.Save();
        await StartRelayAsync(wifi);
        Log?.Invoke(LogKind.Info, wifi ? "Relais ouvert au Wi-Fi : les terminaux du même réseau peuvent s'y connecter." : "Relais limité aux terminaux branchés en USB.");
    }

    private async void RefreshTerminals_Click(object sender, RoutedEventArgs e) => await RefreshTerminalsAsync();

    private void PickAdb_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choisir adb.exe (Android Platform Tools)", Filter = "adb.exe|adb.exe|Programmes|*.exe" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            var settings = AppSettings.Load();
            settings.AdbPath = dialog.FileName;
            settings.Save();
            _ = RefreshTerminalsAsync();
        }
    }

    private async Task RefreshTerminalsAsync()
    {
        _adb = Relay.Adb.Find(AppSettings.Load().AdbPath);
        TerminalList.Children.Clear();
        if (_adb is null)
        {
            AdbStatus.Text = "ADB introuvable : indique où se trouve adb.exe (Platform Tools, outil SAV…). Le Wi-Fi fonctionne sans ADB.";
            return;
        }

        AdbStatus.Text = "ADB : " + _adb;
        IReadOnlyList<Relay.AdbDevice> devices;
        try
        {
            devices = await Relay.Adb.DevicesAsync(_adb);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException)
        {
            AdbStatus.Text = "ADB ne démarre pas : " + ex.Message;
            return;
        }

        if (devices.Count == 0)
        {
            var none = new TextBlock { Text = "Aucun terminal branché. Active le débogage USB (Options pour les développeurs) et branche le câble.", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            none.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            TerminalList.Children.Add(none);
            return;
        }

        foreach (var d in devices)
        {
            var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 8) };
            row.SetResourceReference(Border.BackgroundProperty, "Surface2");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = d.Model, FontWeight = FontWeights.SemiBold });
            var sub = new TextBlock { Text = $"{d.Serial} · {d.StateText}", FontSize = 12, TextWrapping = TextWrapping.Wrap };
            sub.SetResourceReference(TextBlock.ForegroundProperty, d.Ready ? "Muted" : "WarnFg");
            text.Children.Add(sub);
            grid.Children.Add(text);
            var opened = _openedUsb.ContainsKey(d.Serial);
            var open = new Button
            {
                Content = opened ? "Arrêter la liaison" : "Ouvrir le relais",
                Style = (Style)FindResource("BtnSecondary"),
                IsEnabled = d.Ready,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            open.Click += async (_, _) =>
            {
                if (_openedUsb.ContainsKey(d.Serial))
                {
                    await StopUsbRelayAsync(d);
                }
                else
                {
                    await OpenRelayAsync(d);
                }
            };
            Grid.SetColumn(open, 1);
            grid.Children.Add(open);
            row.Child = grid;
            TerminalList.Children.Add(row);
        }
    }

    private async Task OpenRelayAsync(Relay.AdbDevice device)
    {
        if (_adb is null)
        {
            return;
        }

        if (!_relay.IsRunning)
        {
            await StartRelayAsync(WifiCheck.IsChecked == true);
        }

        _relay.UsbModelHint = device.Model;
        _relay.Unblock(device.Model, "USB");
        var error = await Relay.Adb.OpenRelayAsync(_adb, device.Serial);
        if (error is null)
        {
            _openedUsb[device.Serial] = device;
        }

        Log?.Invoke(error is null ? LogKind.Ok : LogKind.Error, error ?? $"Relais ouvert sur {device.Model} : scanne un code avec le terminal, la lecture arrive au banc d'essai.");
        await RefreshTerminalsAsync();
    }

    /// <summary>Coupe la liaison avec un terminal USB : redirection ADB retirée, page bloquée.</summary>
    private async Task StopUsbRelayAsync(Relay.AdbDevice device)
    {
        _openedUsb.Remove(device.Serial);
        _relay.Block(device.Model, "USB");
        if (_adb is not null)
        {
            await Relay.Adb.StopRelayAsync(_adb, device.Serial);
        }

        Log?.Invoke(LogKind.Info, $"Liaison avec {device.Model} arrêtée.");
        await RefreshTerminalsAsync();
    }

    /// <summary>Terminaux reliés : liste, pastille de la barre d'outils.</summary>
    private void OnSessionsChanged()
    {
        var sessions = _relay.Sessions;
        SessionList.Children.Clear();
        foreach (var s in sessions)
        {
            var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 0, 6) };
            row.SetResourceReference(Border.BackgroundProperty, "BrandSoft");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = $"● {s.Model} · relais {s.Origin}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "BrandText");
            grid.Children.Add(label);
            var stop = new Button { Content = "Arrêter la liaison", Style = (Style)FindResource("BtnSmall") };
            stop.Click += async (_, _) =>
            {
                var usb = _openedUsb.Values.FirstOrDefault(d => s.Origin == "USB" && d.Model == s.Model);
                if (usb is not null)
                {
                    await StopUsbRelayAsync(usb);
                }
                else
                {
                    _relay.Block(s.Model, s.Origin);
                    Log?.Invoke(LogKind.Info, $"Liaison avec {s.Model} ({s.Origin}) arrêtée.");
                }
            };
            Grid.SetColumn(stop, 1);
            grid.Children.Add(stop);
            row.Child = grid;
            SessionList.Children.Add(row);
        }

        SessionPanel.Visibility = sessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PublishStatus();
    }

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
        FillCameras(devices.Where(d => d.Kind == ScanSourceKind.Camera));
        devices = [.. devices.Where(d => d.Kind != ScanSourceKind.Camera)];
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

    /// <summary>Caméras : carte à part, chacune avec « Lire en direct ».</summary>
    private void FillCameras(IEnumerable<DeviceEntry> cameras)
    {
        CameraList.Children.Clear();
        foreach (var c in cameras)
        {
            var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 8) };
            row.SetResourceReference(Border.BackgroundProperty, "Surface2");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var live = new Button { Content = "Lire en direct", Style = (Style)FindResource("BtnSecondary"), Margin = new Thickness(10, 0, 0, 0) };
            live.Click += (_, _) => CameraRequested?.Invoke(c.Name);
            Grid.SetColumn(live, 1);
            grid.Children.Add(live);
            row.Child = grid;
            CameraList.Children.Add(row);
        }

        if (CameraList.Children.Count == 0)
        {
            var none = new TextBlock { Text = "Aucune caméra détectée.", FontSize = 12.5 };
            none.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            CameraList.Children.Add(none);
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

    private void OnScanned(ScanEvent e) => Scanned?.Invoke(e);

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
        _deviceStatus = status;
        StatusText.Text = status.Text;
        StatusDot.SetResourceReference(Shape.FillProperty,
            status.Error ? "BadFg" : status.Kind == ScanSourceKind.Keyboard ? "Muted" : "OkFg");
        PublishStatus();
    }

    /// <summary>Source affichée dans la barre d'outils : un terminal relié passe avant la douchette du PC.</summary>
    private void PublishStatus()
    {
        var sessions = _relay.Sessions;
        StatusChanged?.Invoke(sessions.Count switch
        {
            0 => _deviceStatus,
            1 => new ScannerStatus(ScanSourceKind.Relay, $"{sessions[0].Model} · relais {sessions[0].Origin}"),
            _ => new ScannerStatus(ScanSourceKind.Relay, $"{sessions.Count} terminaux reliés"),
        });
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

