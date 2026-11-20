using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;
using Windows11NetworkMonitor.Models;
using Windows11NetworkMonitor.Services;

namespace Windows11NetworkMonitor.Views;

/// <summary>Transparent floating HUD that docks onto the Windows 11 taskbar or screen edges.</summary>
public sealed class HudWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040,
                       SWP_NOACTIVATE = 0x0010;

    private readonly MainWindow _parent;
    private readonly AppSettings _settings;
    private readonly Border _container;
    private readonly TextBlock _speedText;
    private readonly Run _pauseRun;
    private readonly Run _downRun;
    private readonly Run _spacerRun;
    private readonly Run _upRun;
    private readonly DispatcherTimer _repositionTimer;

    private double _lastDownBps;
    private double _lastUpBps;
    private bool _lastPaused;

    public HudWindow(MainWindow parent, AppSettings settings)
    {
        _parent = parent;
        _settings = settings;
        Title = "HUD";

        _container = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(6, 2, 6, 2)
        };
        _container.Tapped += (_, _) => _parent.ShowFromTray();

        _speedText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        _pauseRun = new Run();
        _downRun = new Run { Text = "↓ 0 B/s" };
        _spacerRun = new Run { Text = "  " };
        _upRun = new Run { Text = "↑ 0 B/s" };

        _speedText.Inlines.Add(_pauseRun);
        _speedText.Inlines.Add(_downRun);
        _speedText.Inlines.Add(_spacerRun);
        _speedText.Inlines.Add(_upRun);

        _container.Child = _speedText;
        Content = _container;

        AppWindow.SetPresenter(OverlappedPresenter.Create());
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        ApplySettings();

        _repositionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _repositionTimer.Tick += (_, _) => Reposition();
        _repositionTimer.Start();

        Activated += (_, _) => Reposition();
    }

    public void ApplySettings()
    {
        try
        {
            // 1. Font Family & Size & Weight
            var fontName = string.IsNullOrWhiteSpace(_settings.HudFontFamily) ? "Segoe UI" : _settings.HudFontFamily.Trim();
            _speedText.FontFamily = new FontFamily(fontName);
            var fs = Math.Clamp(_settings.HudFontSize, 8.0, 24.0);
            _speedText.FontSize = fs;
            _speedText.FontWeight = (_settings.HudFontWeight ?? "Normal").Trim().ToLowerInvariant() switch
            {
                "bold" => FontWeights.Bold,
                "semibold" => FontWeights.SemiBold,
                _ => FontWeights.Normal
            };

            // 2. Corner radius & Border
            _container.CornerRadius = new CornerRadius(Math.Clamp(_settings.HudCornerRadius, 0.0, 20.0));

            // 3. Dynamic Window Resizing
            int baseWidth = _settings.HudCompact ? 115 : 158;
            int targetWidth = (int)Math.Max(110, Math.Round(baseWidth * (fs / 11.0)));
            int targetHeight = (int)Math.Max(26, Math.Round(28 * (fs / 11.0)));
            AppWindow.Resize(new Windows.Graphics.SizeInt32(targetWidth, targetHeight));

            // 4. Update Colors
            UpdateThemeColors(force: true);

            // 5. Update Speed Text Format immediately
            RenderSpeedText(_lastDownBps, _lastUpBps, _lastPaused);

            // 6. Reposition to new docked coordinate
            Reposition();
        }
        catch { }
    }

    private string _lastThemeKey = string.Empty;

    private void UpdateThemeColors(bool force = false)
    {
        var mode = string.IsNullOrWhiteSpace(_settings.ThemeMode) ? (_settings.DarkMode ? "Dark" : "System") : _settings.ThemeMode;
        _container.RequestedTheme = mode switch
        {
            "Dark" => ElementTheme.Dark,
            "Light" => ElementTheme.Light,
            _ => ElementTheme.Default
        };

        var resolved = _container.ActualTheme;
        var isDark = mode switch
        {
            "Dark" => true,
            "Light" => false,
            _ => resolved == ElementTheme.Dark
        };

        var scheme = (_settings.HudColorScheme ?? "Auto").Trim();
        var key = $"{mode}:{resolved}:{scheme}:{_settings.HudTextColor}:{_settings.HudDownColor}:{_settings.HudUpColor}:{_settings.HudBgColor}:{_settings.HudBgOpacity}:{_settings.HudShowBorder}:{_settings.HudBorderColor}";
        if (!force && key == _lastThemeKey) return;
        _lastThemeKey = key;

        Windows.UI.Color defaultText = isDark ? Colors.White : Colors.Black;
        Windows.UI.Color defaultBg = isDark ? Colors.Black : Colors.White;
        double defaultOpacity = isDark ? 0.70 : 0.85;

        Windows.UI.Color textColor;
        Windows.UI.Color downColor;
        Windows.UI.Color upColor;
        Windows.UI.Color bgColor;
        double opacity = Math.Clamp(_settings.HudBgOpacity, 0.0, 1.0);

        if (!string.IsNullOrWhiteSpace(_settings.HudTextColor) ||
            !string.IsNullOrWhiteSpace(_settings.HudDownColor) ||
            !string.IsNullOrWhiteSpace(_settings.HudUpColor) ||
            !string.IsNullOrWhiteSpace(_settings.HudBgColor))
        {
            textColor = ColorHelper.ParseColor(_settings.HudTextColor, defaultText);
            downColor = string.IsNullOrWhiteSpace(_settings.HudDownColor) ? textColor : ColorHelper.ParseColor(_settings.HudDownColor, textColor);
            upColor = string.IsNullOrWhiteSpace(_settings.HudUpColor) ? textColor : ColorHelper.ParseColor(_settings.HudUpColor, textColor);
            bgColor = string.IsNullOrWhiteSpace(_settings.HudBgColor) ? defaultBg : ColorHelper.ParseColor(_settings.HudBgColor, defaultBg);
        }
        else
        {
            switch (scheme.ToLowerInvariant())
            {
                case "cyan":
                case "cyberpunk cyan":
                    textColor = ColorHelper.ParseColor("#00E5FF", Colors.Cyan);
                    downColor = ColorHelper.ParseColor("#38BDF8", Colors.Cyan);
                    upColor = ColorHelper.ParseColor("#00E5FF", Colors.Cyan);
                    bgColor = ColorHelper.ParseColor("#0F172A", Colors.Black);
                    break;
                case "green":
                case "matrix green":
                    textColor = ColorHelper.ParseColor("#00FF66", Colors.Green);
                    downColor = ColorHelper.ParseColor("#4ADE80", Colors.Green);
                    upColor = ColorHelper.ParseColor("#00FF66", Colors.Green);
                    bgColor = ColorHelper.ParseColor("#0A0F0D", Colors.Black);
                    break;
                case "amber":
                case "neon amber":
                    textColor = ColorHelper.ParseColor("#FFB300", Colors.Orange);
                    downColor = ColorHelper.ParseColor("#FCD34D", Colors.Orange);
                    upColor = ColorHelper.ParseColor("#F59E0B", Colors.Orange);
                    bgColor = ColorHelper.ParseColor("#1C1917", Colors.Black);
                    break;
                case "violet":
                case "electric violet":
                    textColor = ColorHelper.ParseColor("#C084FC", Colors.Purple);
                    downColor = ColorHelper.ParseColor("#A855F7", Colors.Purple);
                    upColor = ColorHelper.ParseColor("#E879F9", Colors.Purple);
                    bgColor = ColorHelper.ParseColor("#18181B", Colors.Black);
                    break;
                case "white":
                case "pure white":
                    textColor = Colors.White;
                    downColor = Colors.White;
                    upColor = ColorHelper.ParseColor("#E2E8F0", Colors.White);
                    bgColor = Colors.Black;
                    break;
                default: // Auto
                    textColor = defaultText;
                    downColor = defaultText;
                    upColor = defaultText;
                    bgColor = defaultBg;
                    if (_settings.HudBgOpacity == 0.75) opacity = defaultOpacity;
                    break;
            }
        }

        // Apply background
        if (opacity <= 0.001)
        {
            _container.Background = new SolidColorBrush(Colors.Transparent);
        }
        else
        {
            _container.Background = new SolidColorBrush(bgColor) { Opacity = opacity };
        }

        // Apply text brushes
        _downRun.Foreground = new SolidColorBrush(downColor);
        _upRun.Foreground = new SolidColorBrush(upColor);
        _spacerRun.Foreground = new SolidColorBrush(textColor);
        _pauseRun.Foreground = new SolidColorBrush(textColor);

        // Apply border
        if (_settings.HudShowBorder)
        {
            var fallbackBorder = isDark ? Windows.UI.Color.FromArgb(80, 255, 255, 255) : Windows.UI.Color.FromArgb(80, 0, 0, 0);
            _container.BorderBrush = new SolidColorBrush(ColorHelper.ParseColor(_settings.HudBorderColor, fallbackBorder));
            _container.BorderThickness = new Thickness(1);
        }
        else
        {
            _container.BorderBrush = null;
            _container.BorderThickness = new Thickness(0);
        }
    }

    private void Reposition()
    {
        try
        {
            var hwndSelf = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwndSelf != IntPtr.Zero)
            {
                // Auto-hide the HUD if any foreground window is fullscreen on this monitor
                var fg = GetForegroundWindow();
                if (fg != IntPtr.Zero && fg != hwndSelf)
                {
                    var hMon = MonitorFromWindow(hwndSelf, 2 /* MONITOR_DEFAULTTONEAREST */);
                    var mon = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(hMon, ref mon) && GetWindowRect(fg, out var fgRect))
                    {
                        bool isFullscreen = fgRect.Left <= mon.rcMonitor.Left &&
                                            fgRect.Top <= mon.rcMonitor.Top &&
                                            fgRect.Right >= mon.rcMonitor.Right &&
                                            fgRect.Bottom >= mon.rcMonitor.Bottom;
                        if (isFullscreen)
                        {
                            ShowWindow(hwndSelf, 0 /* SW_HIDE */);
                            return;
                        }
                    }
                }

                var ex = GetWindowLong(hwndSelf, GWL_EXSTYLE);
                SetWindowLong(hwndSelf, GWL_EXSTYLE, ex | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
                // SWP_NOACTIVATE is essential: without it this periodic call could
                // activate the HUD anyway and steal focus from whatever the user
                // is doing (e.g. clicking "New" in Snipping Tool mid-snip).
                SetWindowPos(hwndSelf, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW | SWP_NOACTIVATE);
            }
            TaskbarHudPosition.Dock(AppWindow, hwndSelf, _settings.HudDock, _settings.HudOffsetX, _settings.HudOffsetY);
            UpdateThemeColors();
        }
        catch { }
    }

    private void RenderSpeedText(double downBps, double upBps, bool paused)
    {
        var p = paused ? "⏸ " : string.Empty;
        _pauseRun.Text = p;
        if (_settings.HudCompact)
        {
            _downRun.Text = $"↓{SpeedFormatter.FormatCompact(downBps)}";
            _spacerRun.Text = " ";
            _upRun.Text = $"↑{SpeedFormatter.FormatCompact(upBps)}";
        }
        else
        {
            _downRun.Text = $"↓ {SpeedFormatter.Format(downBps)}";
            _spacerRun.Text = "  ";
            _upRun.Text = $"↑ {SpeedFormatter.Format(upBps)}";
        }
    }

    public void Update(double downBps, double upBps, bool paused = false)
    {
        _lastDownBps = downBps;
        _lastUpBps = upBps;
        _lastPaused = paused;
        DispatcherQueue.TryEnqueue(() => RenderSpeedText(downBps, upBps, paused));
    }
}
