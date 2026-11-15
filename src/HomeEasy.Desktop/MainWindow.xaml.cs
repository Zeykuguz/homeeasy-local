using System.ComponentModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HomeEasy.Protocol;
using Forms = System.Windows.Forms;

namespace HomeEasy.Desktop;

public partial class MainWindow : Window
{
    private readonly Settings _settings = Settings.Load();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Forms.NotifyIcon _tray = new();
    private AcClient? _client;
    private AcState? _state;
    private bool _updatingUi;
    private bool _exiting;
    private bool _connectionError;

    private static readonly Dictionary<string, int> TimerPresets = new()
    {
        ["15 min"] = 15, ["30 min"] = 30, ["45 min"] = 45, ["1 h"] = 60, ["1.5 h"] = 90, ["2 h"] = 120,
        ["3 h"] = 180, ["4 h"] = 240, ["5 h"] = 300, ["6 h"] = 360, ["8 h"] = 480, ["10 h"] = 600, ["12 h"] = 720,
    };

    public MainWindow()
    {
        InitializeComponent();
        ModeBox.ItemsSource = Enum.GetValues<AcMode>();
        FanBox.ItemsSource = Enum.GetValues<FanSetting>();
        VSwingBox.ItemsSource = Enum.GetValues<VerticalSwing>();
        HSwingBox.ItemsSource = Enum.GetValues<HorizontalSwing>();
        TimerBox.ItemsSource = TimerPresets.Keys;
        TimerBox.SelectedIndex = 3;
        HostBox.Text = _settings.Host;
        _poll.Tick += async (_, _) => await RefreshAsync();
        SetupTray();
        Loaded += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_settings.Host)) await ConnectAsync(_settings.Host);
            else await DiscoverAsync();
        };
    }

    private void SetupTray()
    {
        _tray.Icon = CreateTrayIcon();
        _tray.Text = "HomeEasy Local";
        _tray.Visible = true;
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowFromTray(); };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        menu.Items.Add("Power ON", null, async (_, _) => await ChangeAsync(s => s.Power = true));
        menu.Items.Add("Power OFF", null, async (_, _) => await ChangeAsync(s => s.Power = false));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.FillEllipse(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0, 120, 215)), 0, 0, 31, 31);
            using var font = new System.Drawing.Font("Segoe UI", 11, System.Drawing.FontStyle.Bold);
            var fmt = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center };
            g.DrawString("AC", font, System.Drawing.Brushes.White, new System.Drawing.RectangleF(0, 0, 32, 32), fmt);
        }
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_exiting && _settings.CloseToTray && _client != null)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(3000, "HomeEasy Local",
                "Still running in the tray (blue AC icon, maybe under ^). Click it, or start the app again, to reopen.",
                Forms.ToolTipIcon.Info);
            return;
        }
        _poll.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _client?.DisposeAsync().AsTask().Wait(1000);
    }

    private async void Discover_Click(object sender, RoutedEventArgs e) => await DiscoverAsync();

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync(HostBox.Text.Trim());

    private async Task DiscoverAsync()
    {
        DiscoverButton.IsEnabled = false;
        SetStatus("Searching the local network…");
        try
        {
            var devices = await Discovery.DiscoverAsync(TimeSpan.FromSeconds(4));
            HostBox.ItemsSource = devices.Select(d => d.Address.ToString()).ToList();
            if (devices.Count == 0)
            {
                SetStatus("No AC found. PC and AC must be on the same network (KODI). Set the Windows network to 'Private', or type the IP.", error: true);
                return;
            }
            SetStatus($"Found: {string.Join(", ", devices.Select(d => $"{d.Address} (MAC {d.Mac})"))}");
            HostBox.Text = devices[0].Address.ToString();
            _settings.Mac = devices[0].Mac;
            await ConnectAsync(HostBox.Text);
        }
        catch (Exception ex)
        {
            SetStatus($"Discovery failed: {ex.Message}", error: true);
        }
        finally
        {
            DiscoverButton.IsEnabled = true;
        }
    }

    private async Task ConnectAsync(string host)
    {
        if (!IPAddress.TryParse(host, out _))
        {
            SetStatus("Enter a valid IP address, e.g. 192.168.0.5", error: true);
            return;
        }
        _poll.Stop();
        if (_client != null) await _client.DisposeAsync();
        _client = new AcClient(host);
        SetStatus($"Connecting to {host}…");
        if (await RefreshAsync())
        {
            _settings.Host = host;
            _settings.Save();
        }
        _poll.Start();
    }

    private async Task<bool> RefreshAsync()
    {
        if (_client == null) return false;
        try
        {
            ShowState(await _client.GetStatusAsync());
            if (_connectionError || StatusText.Text.StartsWith("Connecting"))
                SetStatus($"Connected to {HostBox.Text}");
            _connectionError = false;
            return true;
        }
        catch (Exception ex)
        {
            _connectionError = true;
            ControlsPanel.IsEnabled = false;
            SetStatus($"Cannot reach AC at {HostBox.Text}: {ex.Message}", error: true);
            return false;
        }
    }

    private async Task ChangeAsync(Action<AcState> change)
    {
        if (_client == null || _updatingUi) return;
        _poll.Stop();
        try
        {
            ShowState(await _client.UpdateAsync(change));
            SetStatus($"Updated at {DateTime.Now:T}");
        }
        catch (Exception ex)
        {
            SetStatus($"Command failed: {ex.Message}", error: true);
            if (_state != null) ShowState(_state);
        }
        finally
        {
            _poll.Start();
        }
    }

    private void ShowState(AcState s)
    {
        _state = s;
        _updatingUi = true;
        try
        {
            ControlsPanel.IsEnabled = true;
            IndoorText.Text = $"{s.IndoorCelsius:0.0} °C";
            SetpointText.Text = $"{s.SetpointCelsius} °C";
            PowerButton.IsChecked = s.Power;
            PowerButton.Content = s.Power ? "ON" : "OFF";
            PowerButton.Background = s.Power ? Brushes.LightGreen : Brushes.LightGray;
            ModeBox.SelectedItem = s.Mode;
            FanBox.SelectedItem = s.Fan;
            VSwingBox.SelectedItem = s.VerticalSwing;
            HSwingBox.SelectedItem = s.HorizontalSwing;
            EcoBox.IsChecked = s.Eco;
            SleepBox.IsChecked = s.Sleep;
            DisplayBox.IsChecked = s.Display;
            HealthBox.IsChecked = s.Health;
            AuxBox.IsChecked = s.Aux;
            if (s.OffTimerEnabled && s.OffTimerMinutes > 0 && s.Power)
            {
                var at = DateTime.Now.AddMinutes(s.OffTimerMinutes);
                TimerText.Text = $"Off timer: turns off in {FormatMinutes(s.OffTimerMinutes)} (about {at:HH:mm})";
                TimerText.Foreground = Brushes.DarkBlue;
            }
            else
            {
                TimerText.Text = "Off timer: not set";
                TimerText.Foreground = Brushes.DimGray;
            }
            var tip = $"AC {(s.Power ? "ON" : "OFF")} {s.Mode} {s.SetpointCelsius}°C, room {s.IndoorCelsius:0.0}°C";
            _tray.Text = tip.Length > 63 ? tip[..63] : tip;
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = error ? Brushes.Firebrick : Brushes.DimGray;
    }

    private static string FormatMinutes(int m) => m >= 60 ? $"{m / 60} h {m % 60:00} min" : $"{m} min";

    private static int? ParseTimer(string text)
    {
        text = text.Trim().ToLowerInvariant();
        if (TimerPresets.TryGetValue(text, out int preset)) return preset;
        var num = text.Replace("min", "").Replace("m", "").Trim();
        if (text.EndsWith("h") && double.TryParse(text.TrimEnd('h').Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double hours))
            return (int)Math.Round(hours * 60);
        return int.TryParse(num, out int minutes) ? minutes : null;
    }

    private async void TimerStart_Click(object sender, RoutedEventArgs e)
    {
        var minutes = ParseTimer(TimerBox.Text);
        if (minutes is null or < 1 or > 1440)
        {
            SetStatus("Timer: choose a preset or type minutes (1–1440), e.g. 90 or 1.5 h", error: true);
            return;
        }
        await ChangeAsync(s => s.SetOffTimer(minutes));
    }

    private async void TimerCancel_Click(object sender, RoutedEventArgs e) =>
        await ChangeAsync(s => s.SetOffTimer(null));

    private async void Power_Click(object sender, RoutedEventArgs e) =>
        await ChangeAsync(s => s.Power = !s.Power);

    private async void TempUp_Click(object sender, RoutedEventArgs e) =>
        await ChangeAsync(s => s.SetpointCelsius = Math.Min(30, s.SetpointCelsius + 1));

    private async void TempDown_Click(object sender, RoutedEventArgs e) =>
        await ChangeAsync(s => s.SetpointCelsius = Math.Max(16, s.SetpointCelsius - 1));

    private async void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ModeBox.SelectedItem is AcMode m) await ChangeAsync(s => s.Mode = m);
    }

    private async void Fan_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FanBox.SelectedItem is FanSetting f) await ChangeAsync(s => s.Fan = f);
    }

    private async void VSwing_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (VSwingBox.SelectedItem is VerticalSwing v) await ChangeAsync(s => s.VerticalSwing = v);
    }

    private async void HSwing_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (HSwingBox.SelectedItem is HorizontalSwing h) await ChangeAsync(s => s.HorizontalSwing = h);
    }

    private async void Flag_Click(object sender, RoutedEventArgs e)
    {
        bool on = ((CheckBox)sender).IsChecked == true;
        Action<AcState> change =
            sender == EcoBox ? s => s.Eco = on :
            sender == SleepBox ? s => s.Sleep = on :
            sender == DisplayBox ? s => s.Display = on :
            sender == HealthBox ? s => s.Health = on :
            s => s.Aux = on;
        await ChangeAsync(change);
    }
}
