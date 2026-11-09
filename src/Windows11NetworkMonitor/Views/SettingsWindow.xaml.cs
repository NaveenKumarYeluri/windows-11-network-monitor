using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows11NetworkMonitor.Models;
using Windows11NetworkMonitor.Services;

namespace Windows11NetworkMonitor.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IReadOnlyList<NetworkAdapterInfo> _adapters;
    private readonly SettingsService _store = new();
    private readonly HistoryStore _history = new();

    public SettingsWindow(AppSettings settings, IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        _settings = settings;
        _adapters = adapters;
        InitializeComponent();
        Title = "Settings";
        SetWindowIcon();
        // Settings dialog stays Light so controls remain readable regardless of Main dark/light choice
        ((FrameworkElement)Content).RequestedTheme = ElementTheme.Light;
        AppWindow.Resize(new SizeInt32(620, 640));
        _history.Load();
        LoadValues();
    }

    private void LoadValues()
    {
        AdapterModeCombo.Items.Clear();
        AdapterModeCombo.Items.Add("Auto");
        AdapterModeCombo.Items.Add("Combined");
        foreach (var a in _adapters)
        {
            if (string.IsNullOrWhiteSpace(a.Name)) continue;
            if (!AdapterModeCombo.Items.Contains(a.Name))
                AdapterModeCombo.Items.Add(a.Name);
        }

        var wanted = (string.IsNullOrWhiteSpace(_settings.AdapterMode) ? "Auto" : _settings.AdapterMode.Trim());
        var idx = 0;
        for (int i = 0; i < AdapterModeCombo.Items.Count; i++)
        {
            if (string.Equals(AdapterModeCombo.Items[i]?.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }
        AdapterModeCombo.SelectedIndex = idx;

        var theme = string.IsNullOrWhiteSpace(_settings.ThemeMode) ? (_settings.DarkMode ? "Dark" : "System") : _settings.ThemeMode;
        ThemeCombo.SelectedIndex = theme switch { "Dark" => 2, "Light" => 1, _ => 0 };

        PollIntervalBox.Value = System.Math.Clamp(_settings.PollIntervalMs, 250, 5000);
        HistoryRetentionBox.Value = System.Math.Clamp(_settings.HistoryRetentionDays, 0, 365);
        TrackPerAppCheck.IsChecked = _settings.TrackPerApp;
        ShowHudCheck.IsChecked = _settings.ShowTaskbarHud;
        ShowGraphCheck.IsChecked = _settings.ShowGraph;
        IncludeVirtualCheck.IsChecked = _settings.IncludeVirtualAdapters;
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        StatusText.Text = $"Saved to {SettingsService.SettingsFilePath}";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = AdapterModeCombo.Text?.Trim();
        if (string.IsNullOrWhiteSpace(selected) && AdapterModeCombo.SelectedItem is string s)
            selected = s.Trim();
        _settings.AdapterMode = string.IsNullOrWhiteSpace(selected) ? "Auto" : selected!;
        // Preserve canonical casing for Auto/Combined so polling matches regardless of typing case
        if (string.Equals(_settings.AdapterMode, "auto", StringComparison.OrdinalIgnoreCase)) _settings.AdapterMode = "Auto";
        if (string.Equals(_settings.AdapterMode, "combined", StringComparison.OrdinalIgnoreCase)) _settings.AdapterMode = "Combined";

        var interval = (int)Math.Round(PollIntervalBox.Value);
        _settings.PollIntervalMs = System.Math.Clamp(interval, 250, 5000);
        _settings.HistoryRetentionDays = System.Math.Clamp((int)Math.Round(HistoryRetentionBox.Value), 0, 365);
        _settings.TrackPerApp = TrackPerAppCheck.IsChecked == true;
        _settings.ShowTaskbarHud = ShowHudCheck.IsChecked == true;
        _settings.ShowGraph = ShowGraphCheck.IsChecked == true;
        _settings.IncludeVirtualAdapters = IncludeVirtualCheck.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.ThemeMode = ThemeCombo.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        _settings.DarkMode = _settings.ThemeMode == "Dark";

        _store.Save(_settings);
        _store.ApplyStartWithWindows(_settings.StartWithWindows);
        _history.Load();
        _history.Prune(_settings.HistoryRetentionDays);
        // Keep Settings dialog Light — MainWindow picks up ThemeMode on its next tick
        StatusText.Text = "Saved. Adapter mode applies immediately; poll interval takes effect after restart.";
        AdapterModeCombo.Focus(FocusState.Programmatic);
    }

    public void BringToFront()
    {
        Activate();
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
}
