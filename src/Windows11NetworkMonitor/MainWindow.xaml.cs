using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows11NetworkMonitor.Models;
using Windows11NetworkMonitor.Services;
using Windows11NetworkMonitor.Views;

namespace Windows11NetworkMonitor;

public sealed partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly NetworkAdapterService _adapters;
    private readonly HistoryStore _historyStore = new();
    private readonly PerAppTracker _perAppTracker = new();
    private readonly SpeedHistory _history = new();
    private int _pollTick;
    private readonly PeriodicTimer _latencyTimer = new(TimeSpan.FromSeconds(3));
    private double _lastDown, _lastUp;
    private long _latencyMs = -1;

    private TrayIconService? _tray;
    private HudWindow? _hud;
    private SettingsWindow? _settingsWin;
    private bool _exiting;

    public MainWindow() : this(new AppSettings(), new NetworkAdapterService(new AppSettings()))
    {
    }

    public MainWindow(AppSettings settings, NetworkAdapterService adapters)
    {
        _settings = settings;
        _adapters = adapters;
        _historyStore.Load();
        _historyStore.Prune(_settings.HistoryRetentionDays);
        InitializeComponent();
        Title = "Windows 11 Network Monitor";
        ((FrameworkElement)Content).RequestedTheme = GetWantTheme();
        AppWindow.Resize(new SizeInt32(1180, 640));
        AppWindow.Closing += AppWindow_Closing;
        _adapters.Changed += OnSpeedChanged;
        Closed += MainWindow_Closed;
        _ = LatencyLoopAsync();
    }

    public void InitTray(IntPtr hwnd)
    {
        _tray = new TrayIconService(hwnd);
        if (_settings.ShowTrayIcon) _tray.Show();
        _tray.Clicked += () => DispatcherQueue.TryEnqueue(() => ShowDetail());
        _tray.RightClick += () => DispatcherQueue.TryEnqueue(() => ShowSettings());
        if (_settings.ShowTaskbarHud)
        {
            _hud = new HudWindow(this, _settings);
            _hud.Activate();
        }
    }

    private void OnSpeedChanged(IReadOnlyList<NetworkAdapterInfo> all, NetworkAdapterInfo? active, double down, double up)
    {
        _lastDown = down;
        _lastUp = up;

        _settings.SessionDownloadBytes += (long)(down * _settings.PollIntervalMs / 1000);
        _settings.SessionUploadBytes += (long)(up * _settings.PollIntervalMs / 1000);
        var downDelta = (long)(down * _settings.PollIntervalMs / 1000);
        var upDelta = (long)(up * _settings.PollIntervalMs / 1000);
        _historyStore.AddDelta(downDelta, upDelta);
        _settings.DailyDownloadBytes += downDelta;
        _settings.DailyUploadBytes += upDelta;
        _settings.ResetDailyIfNeeded();
        _perAppTracker.TrackPerApp = _settings.TrackPerApp;

        // Every 5th tick, attribute this tick's bytes to visible apps (approx. by connection count)
        if (++_pollTick % 5 == 0 && _settings.TrackPerApp)
        {
            try
            {
                var apps = _perAppTracker.Snapshot();
                var totalConns = apps.Sum(a => a.TotalConnections);
                if (totalConns > 0)
                {
                    foreach (var a in apps)
                    {
                        var share = (double)a.TotalConnections / totalConns;
                        _historyStore.AddPerAppDelta(a.ProcessName, (long)(downDelta * share), (long)(upDelta * share));
                    }
                }
            }
            catch { }
        }

        _history.Add(new SpeedSample { DownloadBps = down, UploadBps = up });

        DispatcherQueue.TryEnqueue(() =>
        {
            DownText.Text = SpeedFormatter.Format(down);
            UpText.Text = SpeedFormatter.Format(up);
            AdapterText.Text = active is null ? "No active adapter" : $"{active.Name} ({(active.IsWiFi ? "WiFi" : active.IsEthernet ? "Ethernet" : "Other")})";
            LatencyText.Text = _latencyMs >= 0 ? $"Latency: {_latencyMs} ms" : "Latency: -- ms";
            SessionText.Text = $"{SpeedFormatter.FormatBytes(_settings.SessionDownloadBytes)} / {SpeedFormatter.FormatBytes(_settings.SessionUploadBytes)}";
            DailyText.Text = $"{SpeedFormatter.FormatBytes(_settings.DailyDownloadBytes)} / {SpeedFormatter.FormatBytes(_settings.DailyUploadBytes)}";
            PeakText.Text = $"{SpeedFormatter.Format(_history.PeakDownloadBps)} / {SpeedFormatter.Format(_history.PeakUploadBps)} / {SpeedFormatter.Format(_history.AvgDownloadBps)}";
            AdapterList.ItemsSource = all;
            // Throttle history lists — recreating them every 1s tick resets ListView scroll (can't see day 6/7)
            if (_pollTick % 30 == 0)
            {
                HistoryList.ItemsSource = _historyStore.Recent(7).Select(r => new { Date = r.Date.ToString("ddd MMM dd"), Down = SpeedFormatter.FormatBytes(r.Down) + " ↓", Up = SpeedFormatter.FormatBytes(r.Up) + " ↑" }).ToList();
                TopAppsList.ItemsSource = _historyStore.TopApps(DateOnly.FromDateTime(DateTime.Now)).Take(5).Select(t => new { App = t.App, Down = SpeedFormatter.FormatBytes(t.Down) + " ↓", Up = SpeedFormatter.FormatBytes(t.Up) + " ↑" }).ToList();
            }
            try
            {
                var appsNow = _perAppTracker.TrackPerApp ? _perAppTracker.Snapshot() : Array.Empty<AppUsage>();
                if (!appsNow.SequenceEqual(AppList.ItemsSource as IEnumerable<AppUsage>))
                    AppList.ItemsSource = appsNow.Select(a => new { a.ProcessName, a.TcpConnections, a.UdpEndpoints, a.TotalConnections, Display = $"{a.ProcessName}  ({a.TcpConnections} TCP + {a.UdpEndpoints} UDP)" }).ToList();
            }
            catch { }
            DrawGraph();

            // Live-apply toggles + dark mode (settings save takes effect without restart)
            var wantTheme = GetWantTheme();
            if (((FrameworkElement)Content).RequestedTheme != wantTheme) ((FrameworkElement)Content).RequestedTheme = wantTheme;
            GraphSection.Visibility = _settings.ShowGraph ? Visibility.Visible : Visibility.Collapsed;
            if (_settings.ShowTrayIcon) { if (_tray is not null && !_tray.IsVisible) _tray.Show(); }
            else { _tray?.Hide(); }
            if (_settings.ShowTaskbarHud)
            {
                if (_hud is null) { _hud = new HudWindow(this, _settings); _hud.Activate(); }
            }
            else
            {
                if (_hud is not null) { _hud.Close(); _hud = null; }
            }

            _tray?.Update(down, up);
            _hud?.Update(down, up);
        });
    }

    private void DrawGraph()
    {
        var samples = _history.Samples;
        if (samples.Count == 0) return;
        var width = GraphCanvas.ActualWidth > 0 ? GraphCanvas.ActualWidth : 800;
        var height = GraphCanvas.ActualHeight > 0 ? GraphCanvas.ActualHeight : 180;
        var max = Math.Max(1, samples.Max(s => Math.Max(s.DownloadBps, s.UploadBps)));
        var mid = max / 2;

        // Y-axis indicators
        MaxLabel.Text = SpeedFormatter.Format(max);
        MidLabel.Text = SpeedFormatter.Format(mid);
        MinLabel.Text = "0 B/s";

        DownLine.Points.Clear();
        UpLine.Points.Clear();
        for (int i = 0; i < samples.Count; i++)
        {
            var x = width * i / Math.Max(1, _history.Samples.Count - 1);
            DownLine.Points.Add(new Point(x, height - (samples[i].DownloadBps / max) * (height - 8) - 4));
            UpLine.Points.Add(new Point(x, height - (samples[i].UploadBps / max) * (height - 8) - 4));
        }
    }

    private async Task LatencyLoopAsync()
    {
        _latencyMs = await WifiService.PingAsync();
        while (await _latencyTimer.WaitForNextTickAsync())
            _latencyMs = await WifiService.PingAsync();
    }

    private void Nav_SelectionChanged(object sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        switch (item.Tag?.ToString())
        {
            case "Dashboard":
                MainScroll.ChangeView(null, 0, 1f);
                break;
            case "Adapters":
                MainScroll.ChangeView(null, (float)AdaptersSection.ActualOffset.Y, 1f, true);
                break;
            case "Apps":
                MainScroll.ChangeView(null, (float)AppsSection.ActualOffset.Y, 1f, true);
                break;
            case "Statistics":
                MainScroll.ChangeView(null, (float)StatsSection.ActualOffset.Y, 1f, true);
                break;
            case "Settings":
                ShowSettings();
                // Settings is a dialog, not a page — reset highlight so re-click can fire again (fixes "staying clicked")
                foreach (var m in Nav.MenuItems)
                {
                    if (m is NavigationViewItem nvi && nvi.Tag?.ToString() == "Dashboard")
                    {
                        Nav.SelectedItem = nvi;
                        break;
                    }
                }
                break;
        }
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettings();
        // Move focus off the button so it doesn't keep a persistent "pressed/focused" accent state
        Nav.Focus(FocusState.Programmatic);
    }

    private ElementTheme GetWantTheme()
    {
        var mode = string.IsNullOrWhiteSpace(_settings.ThemeMode) ? (_settings.DarkMode ? "Dark" : "System") : _settings.ThemeMode;
        return mode switch
        {
            "Dark" => ElementTheme.Dark,
            "Light" => ElementTheme.Light,
            _ => ElementTheme.Default // System
        };
    }

    private void GraphCanvas_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => ShowDetail();

    private void ShowDetail()
    {
        Activate();
    }

    public void ShowSettings()
    {
        var adapters = _adapters.GetAdapters();
        _settingsWin = new SettingsWindow(_settings, adapters);
        _settingsWin.Closed += (_, _) => _settingsWin = null;
        _settingsWin.Activate();
        DispatcherQueue.TryEnqueue(() => _settingsWin.Activate());
    }

    public void Shutdown()
    {
        _exiting = true;
        Close();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_exiting)
        {
            args.Cancel = true;
            sender.Hide();
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs e)
    {
        _tray?.Dispose();
        _hud?.Close();
        _latencyTimer.Dispose();
    }
}
