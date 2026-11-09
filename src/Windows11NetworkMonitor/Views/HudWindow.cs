using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows11NetworkMonitor.Models;
using Windows11NetworkMonitor.Services;

namespace Windows11NetworkMonitor.Views;

/// <summary>Transparent floating HUD that docks onto the Windows 11 taskbar.</summary>
public sealed class HudWindow : Window
{
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040;

    private readonly AppSettings _settings;
    private readonly TextBlock _speedText;
    private readonly DispatcherTimer _repositionTimer;

    public HudWindow(Window parent, AppSettings settings)
    {
        _settings = settings;
        Title = "HUD";

        var root = new Grid
        {
            Padding = new Thickness(6, 2, 6, 2),
            ColumnSpacing = 8
        };
        root.Tapped += (_, _) => parent.Activate();
        _speedText = new TextBlock
        {
            Text = "↓ 0 B/s  ↑ 0 B/s",
            FontSize = 11,
            FontFamily = new FontFamily("Segoe UI")
        };
        root.Children.Add(_speedText);
        Content = root;
        UpdateThemeColors();

        AppWindow.Resize(new Windows.Graphics.SizeInt32(150, 28));
        AppWindow.SetPresenter(OverlappedPresenter.Create());
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        _repositionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _repositionTimer.Tick += (_, _) => Reposition();
        _repositionTimer.Start();

        Activated += (_, _) => Reposition();
    }

    private string _lastThemeKey = string.Empty;

    private void UpdateThemeColors()
    {
        var root = (Grid)Content;
        var mode = string.IsNullOrWhiteSpace(_settings.ThemeMode) ? (_settings.DarkMode ? "Dark" : "System") : _settings.ThemeMode;
        root.RequestedTheme = mode switch
        {
            "Dark" => ElementTheme.Dark,
            "Light" => ElementTheme.Light,
            _ => ElementTheme.Default
        };

        // Colors: Light = white bg / dark text, Dark = dark bg / white text, System follows ActualTheme
        var resolved = root.ActualTheme;
        var isDark = mode switch
        {
            "Dark" => true,
            "Light" => false,
            _ => resolved == ElementTheme.Dark
        };
        var key = $"{mode}:{resolved}";
        if (key == _lastThemeKey) return; // nothing changed → don't churn brushes
        _lastThemeKey = key;

        root.Background = new SolidColorBrush(isDark ? Colors.Black : Colors.White) { Opacity = isDark ? 0.7 : 0.85 };
        _speedText.Foreground = new SolidColorBrush(isDark ? Colors.White : Colors.Black);
    }

    private void Reposition()
    {
        try
        {
            // Re-assert TOPMOST (WinUI's IsAlwaysOnTop alone drops when other apps activate)
            var hwnd = FindWindow(null, "HUD");
            if (hwnd != IntPtr.Zero)
            {
                var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
            var hwndSelf = WinRT.Interop.WindowNative.GetWindowHandle(this);
            TaskbarHudPosition.Dock(AppWindow, hwndSelf, hwnd, _settings.HudDock, _settings.HudOffsetX, _settings.HudOffsetY);
            // Theme colors only need refresh when the resolved theme changes
            // (twice-per-second brush churn was pointless allocation).
            UpdateThemeColors();
        }
        catch { }
    }

    public void Update(double downBps, double upBps)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _speedText.Text = $"↓ {SpeedFormatter.Format(downBps)}  ↑ {SpeedFormatter.Format(upBps)}";
        });
    }
}
