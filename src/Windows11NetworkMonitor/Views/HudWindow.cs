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
using WinRT;

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

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

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
    private bool _transparencyConfigured;

    public HudWindow(MainWindow parent, AppSettings settings)
    {
        _parent = parent;
        _settings = settings;
        Title = "HUD";

        ExtendsContentIntoTitleBar = true;

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

        EnsureWindowTransparency();
        ApplySettings();

        _repositionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _repositionTimer.Tick += (_, _) => Reposition();
        _repositionTimer.Start();

        Activated += (_, _) =>
        {
            EnsureWindowTransparency();
            Reposition();
        };
    }

    private void EnsureWindowTransparency()
    {
        if (_transparencyConfigured) return;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero) return;

            // 1. Extend DWM frame into client area (-1 stretches across entire window)
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            // 2. Set transparent composition brush as system backdrop
            var backdropHolder = this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>();
            var compositor = Microsoft.UI.Xaml.Media.CompositionTarget.GetCompositorForCurrentThread();
            if (backdropHolder != null && compositor != null)
            {
                var transparentBrush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                backdropHolder.SystemBackdrop = transparentBrush;
            }

            _transparencyConfigured = true;
        }
        catch { }
    }

    private void UpdateCornerPreference(double radius)
    {
        try
        {
            var hwndSelf = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwndSelf != IntPtr.Zero)
            {
                int pref = radius <= 0.001 ? DWMWCP_DONOTROUND : (radius <= 4.0 ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND);
                DwmSetWindowAttribute(hwndSelf, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
        }
        catch { }
    }

    public void ApplySettings()
    {
        try
        {
            EnsureWindowTransparency();

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

            // 2. Corner radius & Border & DWM corner preference
            var cr = Math.Clamp(_settings.HudCornerRadius, 0.0, 20.0);
            _container.CornerRadius = new CornerRadius(cr);
            UpdateCornerPreference(cr);

            // 3. Dynamic Window Resizing (DPI-scaled physical pixels)
            var hwndSelf = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var dpi = hwndSelf != IntPtr.Zero ? GetDpiForWindow(hwndSelf) : 96;
            var scale = dpi <= 0 ? 1.0 : dpi / 96.0;

            int baseWidth = _settings.HudCompact ? 115 : 158;
            int targetWidthDip = (int)Math.Max(110, Math.Round(baseWidth * (fs / 11.0)));
            int targetHeightDip = (int)Math.Max(26, Math.Round(28 * (fs / 11.0)));

            int physicalWidth = (int)Math.Round(targetWidthDip * scale);
            int physicalHeight = (int)Math.Round(targetHeightDip * scale);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));

            // 4. Update Colors (force refresh)
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
        var key = $"{mode}:{resolved}:{scheme}:{_settings.HudTextColor}:{_settings.HudDownColor}:{_settings.HudUpColor}:{_settings.HudBgColor}:{_settings.HudBgOpacity}:{_settings.HudCornerRadius}:{_settings.HudShowBorder}:{_settings.HudBorderColor}";
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
            textColor = AppColorHelper.ParseColor(_settings.HudTextColor, defaultText);
            downColor = string.IsNullOrWhiteSpace(_settings.HudDownColor) ? textColor : AppColorHelper.ParseColor(_settings.HudDownColor, textColor);
            upColor = string.IsNullOrWhiteSpace(_settings.HudUpColor) ? textColor : AppColorHelper.ParseColor(_settings.HudUpColor, textColor);
            bgColor = string.IsNullOrWhiteSpace(_settings.HudBgColor) ? defaultBg : AppColorHelper.ParseColor(_settings.HudBgColor, defaultBg);
        }
        else
        {
            switch (scheme.ToLowerInvariant())
            {
                case "cleanwhite":
                    textColor = AppColorHelper.ParseColor("#1E293B", Colors.Black);
                    downColor = AppColorHelper.ParseColor("#0284C7", Colors.Blue);
                    upColor = AppColorHelper.ParseColor("#16A34A", Colors.Green);
                    bgColor = Colors.White;
                    if (_settings.HudBgOpacity == 0.75) opacity = 0.85;
                    break;
                case "frostwhite":
                    textColor = AppColorHelper.ParseColor("#0F172A", Colors.Black);
                    downColor = AppColorHelper.ParseColor("#0284C7", Colors.Blue);
                    upColor = AppColorHelper.ParseColor("#0D9488", Colors.Teal);
                    bgColor = AppColorHelper.ParseColor("#F8FAFC", Colors.White);
                    if (_settings.HudBgOpacity == 0.75) opacity = 0.70;
                    break;
                case "arcticwhite":
                    textColor = AppColorHelper.ParseColor("#334155", Colors.Black);
                    downColor = AppColorHelper.ParseColor("#2563EB", Colors.Blue);
                    upColor = AppColorHelper.ParseColor("#7C3AED", Colors.Purple);
                    bgColor = Colors.White;
                    if (_settings.HudBgOpacity == 0.75) opacity = 0.90;
                    break;
                case "pearllight":
                    textColor = AppColorHelper.ParseColor("#27272A", Colors.Black);
                    downColor = AppColorHelper.ParseColor("#0EA5E9", Colors.Blue);
                    upColor = AppColorHelper.ParseColor("#10B981", Colors.Green);
                    bgColor = AppColorHelper.ParseColor("#F4F4F5", Colors.White);
                    if (_settings.HudBgOpacity == 0.75) opacity = 0.80;
                    break;
                case "whitecard":
                    textColor = AppColorHelper.ParseColor("#111827", Colors.Black);
                    downColor = AppColorHelper.ParseColor("#059669", Colors.Green);
                    upColor = AppColorHelper.ParseColor("#D97706", Colors.Orange);
                    bgColor = Colors.White;
                    if (_settings.HudBgOpacity == 0.75) opacity = 0.95;
                    break;
                case "cyan":
                case "cyberpunk cyan":
                    textColor = AppColorHelper.ParseColor("#00E5FF", Colors.Cyan);
                    downColor = AppColorHelper.ParseColor("#38BDF8", Colors.Cyan);
                    upColor = AppColorHelper.ParseColor("#00E5FF", Colors.Cyan);
                    bgColor = AppColorHelper.ParseColor("#0F172A", Colors.Black);
                    break;
                case "green":
                case "matrix green":
                    textColor = AppColorHelper.ParseColor("#00FF66", Colors.Green);
                    downColor = AppColorHelper.ParseColor("#4ADE80", Colors.Green);
                    upColor = AppColorHelper.ParseColor("#00FF66", Colors.Green);
                    bgColor = AppColorHelper.ParseColor("#0A0F0D", Colors.Black);
                    break;
                case "amber":
                case "neon amber":
                    textColor = AppColorHelper.ParseColor("#FFB300", Colors.Orange);
                    downColor = AppColorHelper.ParseColor("#FCD34D", Colors.Orange);
                    upColor = AppColorHelper.ParseColor("#F59E0B", Colors.Orange);
                    bgColor = AppColorHelper.ParseColor("#1C1917", Colors.Black);
                    break;
                case "violet":
                case "electric violet":
                    textColor = AppColorHelper.ParseColor("#C084FC", Colors.Purple);
                    downColor = AppColorHelper.ParseColor("#A855F7", Colors.Purple);
                    upColor = AppColorHelper.ParseColor("#E879F9", Colors.Purple);
                    bgColor = AppColorHelper.ParseColor("#18181B", Colors.Black);
                    break;
                case "white":
                case "pure white":
                    textColor = Colors.White;
                    downColor = Colors.White;
                    upColor = AppColorHelper.ParseColor("#E2E8F0", Colors.White);
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

        // Apply background with true alpha
        var bgAlpha = (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255);
        if (bgAlpha == 0)
        {
            _container.Background = new SolidColorBrush(Colors.Transparent);
        }
        else
        {
            _container.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(bgAlpha, bgColor.R, bgColor.G, bgColor.B));
        }

        // Apply text brushes
        _downRun.Foreground = new SolidColorBrush(downColor);
        _upRun.Foreground = new SolidColorBrush(upColor);
        _spacerRun.Foreground = new SolidColorBrush(textColor);
        _pauseRun.Foreground = new SolidColorBrush(textColor);

        // Apply border
        if (_settings.HudShowBorder)
        {
            bool isWhiteTheme = scheme.ToLowerInvariant() switch
            {
                "cleanwhite" or "frostwhite" or "arcticwhite" or "pearllight" or "whitecard" => true,
                _ => !isDark
            };
            var fallbackBorder = isWhiteTheme
                ? Windows.UI.Color.FromArgb(160, 203, 213, 225)
                : (isDark ? Windows.UI.Color.FromArgb(80, 255, 255, 255) : Windows.UI.Color.FromArgb(80, 0, 0, 0));
            _container.BorderBrush = new SolidColorBrush(AppColorHelper.ParseColor(_settings.HudBorderColor, fallbackBorder));
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
            EnsureWindowTransparency();
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
