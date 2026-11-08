using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows11NetworkMonitor.Models;
using Windows11NetworkMonitor.Services;

namespace Windows11NetworkMonitor.Views;

public sealed partial class SettingsWindow : Window
{
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040;

    private readonly AppSettings _settings;
    private readonly IReadOnlyList<NetworkAdapterInfo> _adapters;
    private readonly SettingsService _store = new();

    public SettingsWindow() : this(new AppSettings(), Array.Empty<NetworkAdapterInfo>())
    {
    }

    public SettingsWindow(AppSettings settings, IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        _settings = settings;
        _adapters = adapters;
        InitializeComponent();
        Title = "Settings";
        // Settings dialog stays Light so controls remain readable regardless of Main dark/light choice
        ((FrameworkElement)Content).RequestedTheme = ElementTheme.Light;
        AppWindow.Resize(new SizeInt32(620, 560));
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
        ShowTrayCheck.IsChecked = _settings.ShowTrayIcon;
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
        _settings.ShowTrayIcon = ShowTrayCheck.IsChecked == true;
        _settings.ShowTaskbarHud = ShowHudCheck.IsChecked == true;
        _settings.ShowGraph = ShowGraphCheck.IsChecked == true;
        _settings.IncludeVirtualAdapters = IncludeVirtualCheck.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.ThemeMode = ThemeCombo.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        _settings.DarkMode = _settings.ThemeMode == "Dark";

        _store.Save(_settings);
        _store.ApplyStartWithWindows(_settings.StartWithWindows);
        // Keep Settings dialog Light — MainWindow picks up ThemeMode on its next tick
        StatusText.Text = "Saved. Restart the app to apply poll interval / adapter.";
        AdapterModeCombo.Focus(FocusState.Programmatic);
    }

    public void BringToFront()
    {
        Activate();
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var hwnd = FindWindow(null, "Settings");
                if (hwnd != IntPtr.Zero)
                    SetForegroundWindow(hwnd);
            }
            catch { }
        });
    }
}
