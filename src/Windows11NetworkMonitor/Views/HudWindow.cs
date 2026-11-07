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
            Padding = new Thickness(10, 4, 10, 4)
        };
        _speedText = new TextBlock
        {
            Text = "↓ 0 B/s  ↑ 0 B/s",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI")
        };
        root.Children.Add(_speedText);
        Content = root;

        AppWindow.Resize(new Windows.Graphics.SizeInt32(220, 32));
        AppWindow.SetPresenter(OverlappedPresenter.Create());
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMovable = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        _repositionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _repositionTimer.Tick += (_, _) => Reposition();
        _repositionTimer.Start();

        Activated += (_, _) => Reposition();
    }

    private void Reposition()
    {
        try { TaskbarHudPositionDock(); } catch { }
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
