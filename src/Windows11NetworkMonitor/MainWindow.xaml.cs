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
    private readonly SpeedHistory _history = new();
    private readonly PeriodicTimer _latencyTimer = new(TimeSpan.FromSeconds(3));
    private double _lastDown, _lastUp;
    private long _latencyMs = -1;

    private TrayIconService? _tray;
    private HudWindow? _hud;

    public MainWindow() : this(new AppSettings(), new NetworkAdapterService(new AppSettings()))
    {
    }

    public MainWindow(AppSettings settings, NetworkAdapterService adapters)
    {
        _settings = settings;
        _adapters = adapters;
        InitializeComponent();
        Title = "Windows 11 Network Monitor";
        AppWindow.Resize(new SizeInt32(1180, 640));
        _adapters.Changed += OnSpeedChanged;
        Closed += MainWindow_Closed;
        _ = LatencyLoopAsync();
    }

    public void InitTray(IntPtr hwnd)
    {
        _tray = new TrayIconService(hwnd);
        _tray.Show();
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
        _settings.DailyDownloadBytes += (long)(down * _settings.PollIntervalMs / 1000);
        _settings.DailyUploadBytes += (long)(up * _settings.PollIntervalMs / 1000);
        _settings.ResetDailyIfNeeded();

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
            DrawGraph();
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
            case "Statistics":
                MainScroll.ChangeView(null, (float)StatsSection.ActualOffset.Y, 1f, true);
                break;
        }
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void GraphCanvas_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => ShowDetail();

    private void ShowDetail()
    {
        Activate();
    }

    private void ShowSettings()
    {
        var win = new SettingsWindow(_settings);
        win.Activate();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs e)
    {
        _tray?.Dispose();
        _hud?.Close();
        _latencyTimer.Dispose();
    }
}
