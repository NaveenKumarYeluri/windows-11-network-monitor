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
            Background = new SolidColorBrush(Colors.Black) { Opacity = 0.7 },
            Padding = new Thickness(6, 2, 6, 2),
            ColumnSpacing = 8
        };
        root.Tapped += (_, _) => parent.Activate();
        _speedText = new TextBlock
        {
            Text = "↓ 0 B/s  ↑ 0 B/s",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 11,
            FontFamily = new FontFamily("Segoe UI")
        };
        root.Children.Add(_speedText);
        Content = root;

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

    private void Reposition()
    {
        try
        {
            TaskbarHudPositionDock();
            // Re-assert TOPMOST (WinUI's IsAlwaysOnTop alone drops when other apps activate)
            var hwnd = FindWindow(null, "HUD");
            if (hwnd != IntPtr.Zero)
            {
                var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
        }
        catch { }
    }

    private void TaskbarHudPositionDock()
    {
        // Keep it simple: dock via service
        Windows11NetworkMonitor.Services.TaskbarHudPosition.Dock(AppWindow, _settings.HudDock, _settings.HudOffsetX, _settings.HudOffsetY);
    }

    public void Update(double downBps, double upBps)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _speedText.Text = $"↓ {SpeedFormatter.Format(downBps)}  ↑ {SpeedFormatter.Format(upBps)}";
        });
    }
}
