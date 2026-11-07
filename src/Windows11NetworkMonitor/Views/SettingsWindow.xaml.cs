using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows11NetworkMonitor.Models;
using Windows11NetworkMonitor.Services;

namespace Windows11NetworkMonitor.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsService _store = new();

    public SettingsWindow() : this(new AppSettings())
    {
    }

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        Title = "Settings";
        AppWindow.Resize(new SizeInt32(620, 560));
        try
        {
            AppWindow.SetPresenter(Microsoft.UI.Windowing.OverlappedPresenter.Create());
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
                p.IsAlwaysOnTop = true;
        }
        catch { }
        Activated += (_, _) => AppWindow.Move(new Windows.Graphics.PointInt32(Math.Max(0, AppWindow.Position.X), Math.Max(0, AppWindow.Position.Y)));
        LoadValues();
    }

    private void LoadValues()
    {
        AdapterModeBox.Text = _settings.AdapterMode;
        PollIntervalBox.Value = _settings.PollIntervalMs;
        ShowTrayCheck.IsChecked = _settings.ShowTrayIcon;
        ShowHudCheck.IsChecked = _settings.ShowTaskbarHud;
        ShowGraphCheck.IsChecked = _settings.ShowGraph;
        IncludeVirtualCheck.IsChecked = _settings.IncludeVirtualAdapters;
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        DarkModeCheck.IsChecked = _settings.DarkMode;
        StatusText.Text = $"Saved to {SettingsService.SettingsFilePath}";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.AdapterMode = string.IsNullOrWhiteSpace(AdapterModeBox.Text) ? "Auto" : AdapterModeBox.Text.Trim();
        _settings.PollIntervalMs = (int)PollIntervalBox.Value;
        _settings.ShowTrayIcon = ShowTrayCheck.IsChecked == true;
        _settings.ShowTaskbarHud = ShowHudCheck.IsChecked == true;
        _settings.ShowGraph = ShowGraphCheck.IsChecked == true;
        _settings.IncludeVirtualAdapters = IncludeVirtualCheck.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.DarkMode = DarkModeCheck.IsChecked == true;

        _store.Save(_settings);
        _store.ApplyStartWithWindows(_settings.StartWithWindows);
        StatusText.Text = "Saved.";
    }
}
