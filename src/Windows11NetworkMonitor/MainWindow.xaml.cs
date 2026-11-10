using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
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
    private readonly SettingsService _settingsStore = new();
    private readonly PerAppTracker _perAppTracker = new();
    private readonly SpeedHistory _history;
    private int _pollTick;
    private readonly Dictionary<string, double> _perAppAccumDown = new();
    private readonly Dictionary<string, double> _perAppAccumUp = new();
    // Cached rendered text to avoid rebuilding StackPanels (and resetting scroll) when unchanged.
    // Using strings (not byte sums) so tiny 100-byte increments don't count as "changed" until visible text differs.
    private string _lastHistoryCsv = string.Empty;
    private string _lastTopCsv = string.Empty;
    private string _lastAppCsv = string.Empty;
    private string _lastAdapterCsv = string.Empty;
    private readonly PeriodicTimer _latencyTimer = new(TimeSpan.FromSeconds(3));
    private long _latencyMs = -1;
    private IReadOnlyList<AppUsage> _latestApps = Array.Empty<AppUsage>();

    private TrayIconService? _tray;
    private HudWindow? _hud;
    private SettingsWindow? _settingsWin;
    private bool _exiting;

    public MainWindow(AppSettings settings, NetworkAdapterService adapters)
    {
        _settings = settings;
        _adapters = adapters;
        // Keep the graph a true "last minute" window regardless of the poll
        // interval: 60 s / interval samples, clamped to a sane range.
        _history = new SpeedHistory(Math.Clamp(60000 / Math.Max(1, _settings.PollIntervalMs), 30, 240));
        _historyStore.Load();
        _historyStore.Prune(_settings.HistoryRetentionDays);
        InitializeComponent();
        Title = "Windows 11 Network Monitor";
        SetWindowIcon();
        ((FrameworkElement)Content).RequestedTheme = GetWantTheme();
        AppWindow.Resize(new SizeInt32(1180, 640));
        AppWindow.Closing += AppWindow_Closing;
        _adapters.Changed += OnSpeedChanged;
        Closed += MainWindow_Closed;
        _ = LatencyLoopAsync();
    }

    public void InitTray()
    {
        // Use our own window's handle — GetForegroundWindow() can race and return
        // some other application's window right after activation.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _tray = new TrayIconService(hwnd);
        if (_settings.ShowTrayIcon) _tray.Show();
        _tray.Clicked += () => DispatcherQueue.TryEnqueue(() => ShowDetail());
        _tray.IsPaused = () => _settings.TrackingPaused;
        _tray.MenuShowDashboard += () => DispatcherQueue.TryEnqueue(() => ShowDetail());
        _tray.MenuTogglePause += () =>
        {
            _settings.TrackingPaused = !_settings.TrackingPaused;
            _settingsStore.Save(_settings);
        };
        _tray.MenuShowSettings += () => DispatcherQueue.TryEnqueue(() => ShowSettings());
        _tray.MenuCheckUpdates += () => DispatcherQueue.TryEnqueue(() => CheckUpdatesNow(manual: true));
        _tray.BalloonClicked += () => DispatcherQueue.TryEnqueue(OpenReleasePage);
        _tray.MenuExit += () => DispatcherQueue.TryEnqueue(Shutdown);
        if (_settings.ShowTaskbarHud)
        {
            _hud = new HudWindow(this, _settings);
            _hud.Activate();
        }
    }

    private string? _releaseUrl;

    /// <summary>
    /// Startup update check (GitHub Releases API). Runs 5 s after launch so
    /// startup isn't gated on network I/O; silent when up to date or offline.
    /// </summary>
    public void RunUpdateChecks()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                var info = await UpdateChecker.CheckAsync();
                if (info is null) return;
                _releaseUrl = info.Url;
                DispatcherQueue.TryEnqueue(() => _tray?.ShowBalloon(
                    $"New version {info.Tag} available",
                    $"Windows 11 Network Monitor {UpdateChecker.CurrentVersion().ToString(3)} is installed. Click this notification to view the new release."));
            }
            catch { /* offline or rate-limited: stay silent at startup */ }
        });
    }

    /// <summary>Manual "Check for updates" (tray menu): always reports a result via balloon.</summary>
    private async void CheckUpdatesNow(bool manual)
    {
        try
        {
            var info = await UpdateChecker.CheckAsync();
            if (info is not null)
            {
                _releaseUrl = info.Url;
                _tray?.ShowBalloon($"New version {info.Tag} available", "Click this notification to open the release page on GitHub.");
            }
            else if (manual)
            {
                _tray?.ShowBalloon("You're up to date", $"Windows 11 Network Monitor {UpdateChecker.CurrentVersion().ToString(3)} is the latest release.");
            }
        }
        catch (Exception ex)
        {
            if (manual) _tray?.ShowBalloon("Update check failed", ex.Message);
        }
    }

    private void OpenReleasePage()
    {
        var url = _releaseUrl ?? UpdateChecker.ReleasesUrl;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    private void OnSpeedChanged(IReadOnlyList<NetworkAdapterInfo> all, NetworkAdapterInfo? active, double down, double up, double elapsedSecs)
    {
        // Deltas use the REAL elapsed seconds since the previous snapshot, so
        // totals stay accurate when ticks lag (adapter enumeration, per-app
        // snapshotting, sleep/wake).
        // Pause: stop accumulating session/daily/history/per-app counters only.
        // Live speed display, graph, tray and HUD keep updating so the link state
        // stays visible while paused.
        var paused = _settings.TrackingPaused;
        var downDelta = paused ? 0 : (long)(down * elapsedSecs);
        var upDelta = paused ? 0 : (long)(up * elapsedSecs);
        _settings.SessionDownloadBytes += downDelta;
        _settings.SessionUploadBytes += upDelta;
        _historyStore.AddDelta(downDelta, upDelta);
        _settings.DailyDownloadBytes += downDelta;
        _settings.DailyUploadBytes += upDelta;
        _settings.ResetDailyIfNeeded();
        _perAppTracker.TrackPerApp = _settings.TrackPerApp;
        ++_pollTick;

        // Snapshot once; reused for both attribution and the "Apps using network now" UI below.
        IReadOnlyList<AppUsage> latestApps = Array.Empty<AppUsage>();
        if (_settings.TrackPerApp)
        {
            try { latestApps = _perAppTracker.Snapshot(); _latestApps = latestApps; } catch { latestApps = _latestApps; }
        }
        else { _latestApps = Array.Empty<AppUsage>(); }

        // Attribute THIS tick's bytes (every tick, not every 5th) proportionally to connection share
        // with fractional remainder accumulation so tiny shares aren't truncated to 0 and lost.
        if (latestApps.Count > 0 && (downDelta > 0 || upDelta > 0))
        {
            try
            {
                var totalConns = latestApps.Sum(a => a.TotalConnections);
                if (totalConns > 0)
                {
                    foreach (var a in latestApps)
                    {
                        var share = (double)a.TotalConnections / totalConns;
                        var key = a.ProcessName;
                        _perAppAccumDown.TryGetValue(key, out var dAcc);
                        _perAppAccumUp.TryGetValue(key, out var uAcc);
                        dAcc += downDelta * share;
                        uAcc += upDelta * share;
                        var dFlush = (long)dAcc;
                        var uFlush = (long)uAcc;
                        if (dFlush > 0 || uFlush > 0)
                        {
                            _historyStore.AddPerAppDelta(key, dFlush, uFlush);
                            dAcc -= dFlush;
                            uAcc -= uFlush;
                        }
                        _perAppAccumDown[key] = dAcc;
                        _perAppAccumUp[key] = uAcc;
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
            SessionText.Text = $"{SpeedFormatter.FormatBytes(_settings.SessionDownloadBytes)} / {SpeedFormatter.FormatBytes(_settings.SessionUploadBytes)}{(paused ? "  (paused)" : "")}";
            DailyText.Text = $"{SpeedFormatter.FormatBytes(_settings.DailyDownloadBytes)} / {SpeedFormatter.FormatBytes(_settings.DailyUploadBytes)}";
            PeakText.Text = $"{SpeedFormatter.Format(_history.PeakDownloadBps)} / {SpeedFormatter.Format(_history.PeakUploadBps)} / {SpeedFormatter.Format(_history.AvgDownloadBps)}";
            // Render into StackPanels (no inner ListView scroll). Each updater only repaints
            // when the *visible text* changes and restores outer scroll offset, so rows
            // 6/7 stay reachable and the page never snaps to day 1 on its own.
            UpdateAdapters(all);
            UpdateHistory();
            UpdateTopApps();
            UpdateApps();
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
            _hud?.Update(down, up, paused);
        });
    }

    // --- StackPanel updaters (fix for "history keeps snapping to day 1") ---
    // Previously each tick replaced ListView.ItemsSource, which resets that ListView's
    // internal VerticalOffset to 0. With 7 rows hidden behind MaxHeight the offset reset
    // made days 6/7 unreachable. These updaters render plain TextBlocks into a
    // StackPanel inside the single outer ScrollViewer (MainScroll), never create an
    // inner scrollbar, compare *rendered strings* (not raw byte sums) and restore the
    // outer offset after a repaint, so the page never scrolls on its own.

    private void UpdateAdapters(IReadOnlyList<NetworkAdapterInfo> all)
    {
        var lines = all.Select(a =>
            (a.IsUp ? "● " : "○ ") + $"{a.Name} — {a.Description}  ({(a.IsWiFi ? "WiFi" : a.IsEthernet ? "Ethernet" : "Other")})")
            .ToArray();
        RenderInto(AdapterPanel, ref _lastAdapterCsv, lines, saveOffset: true);
    }

    private void UpdateHistory()
    {
        var recent = _historyStore.Recent(7);
        // Render newest day first is left as-is (oldest→newest matches earlier build);
        // all 7 rows are now visible via outer scroll, no inner scrollbar needed.
        var lines = recent.Select(r =>
            $"{r.Date:ddd MMM dd}    {SpeedFormatter.FormatBytes(r.Down)} ↓    {SpeedFormatter.FormatBytes(r.Up)} ↑")
            .ToArray();
        RenderInto(HistoryPanel, ref _lastHistoryCsv, lines, saveOffset: true);
    }

    private void UpdateTopApps()
    {
        var top = _historyStore.TopApps(DateOnly.FromDateTime(DateTime.Now)).Take(5).ToList();
        string[] lines;
        if (top.Count == 0)
        {
            // Explicit placeholder so "blank" isn't a mystery — it means no attributed bytes yet.
            lines = new[]
            {
                "No per-app data yet.",
                "Keep “Track per-app usage” ON, browse/stream/Teams for ~1 min, then check back (data is estimated by connection share)."
            };
        }
        else
        {
            lines = top.Select(t =>
                $"{t.App}    {SpeedFormatter.FormatBytes(t.Down)} ↓    {SpeedFormatter.FormatBytes(t.Up)} ↑")
                .ToArray();
        }
        RenderInto(TopAppsPanel, ref _lastTopCsv, lines, saveOffset: true);
    }

    private void UpdateApps()
    {
        var apps = _latestApps;
        string[] lines;
        if (apps.Count == 0)
        {
            lines = _settings.TrackPerApp
                ? new[] { "No live connections detected (idle?)" }
                : new[] { "Per-app tracking is OFF — enable it in Settings." };
        }
        else
        {
            lines = apps.Take(20).Select(a =>
                $"{a.ProcessName}  ({a.TcpConnections} TCP + {a.UdpEndpoints} UDP)").ToArray();
        }
        RenderInto(AppPanel, ref _lastAppCsv, lines, saveOffset: true);
    }

    private void RenderInto(StackPanel panel, ref string lastCsv, string[] lines, bool saveOffset)
    {
        var csv = string.Join("\n", lines);
        if (string.Equals(csv, lastCsv, StringComparison.Ordinal)) return; // unchanged → don't churn layout/scroll
        var off = saveOffset ? MainScroll.VerticalOffset : 0;
        panel.Children.Clear();
        foreach (var line in lines)
        {
            panel.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.9
            });
        }
        lastCsv = csv;
        if (saveOffset)
        {
            // Restoring the offset synchronously prevents the outer ScrollViewer from
            // re-anchoring to the top when a child panel's height changes.
            MainScroll.ChangeView(null, (float)off, 1f, true);
        }
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
            var x = width * i / Math.Max(1, samples.Count - 1);
            DownLine.Points.Add(new Point(x, height - (samples[i].DownloadBps / max) * (height - 8) - 4));
            UpLine.Points.Add(new Point(x, height - (samples[i].UploadBps / max) * (height - 8) - 4));
        }
    }

    private async Task LatencyLoopAsync()
    {
        try
        {
            _latencyMs = await WifiService.PingAsync();
            while (true)
            {
                _latencyMs = await WifiService.PingAsync();
                await _latencyTimer.WaitForNextTickAsync();
            }
        }
        catch (ObjectDisposedException) { } // timer disposed while waiting (window closed)
        catch (OperationCanceledException) { }
        catch { }
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

    private void SetWindowIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "NetworkMonitor.ico");
            if (System.IO.File.Exists(path)) AppWindow.SetIcon(path);
        }
        catch { }
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
        try { _historyStore.Save(); } catch { }
        try { _settingsStore.Save(_settings); } catch { } // persists session/daily counters
        _tray?.Dispose();
        _hud?.Close();
        _latencyTimer.Dispose();
        _adapters.Dispose();
    }
}
