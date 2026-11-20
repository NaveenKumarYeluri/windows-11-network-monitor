using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI.Text;
using Windows11NetworkMonitor.Models;
using Windows11NetworkMonitor.Services;

namespace Windows11NetworkMonitor.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IReadOnlyList<NetworkAdapterInfo> _adapters;
    private readonly SettingsService _store = new();
    private readonly HistoryStore _history;
    private readonly Action? _onSettingsSaved;

    private readonly Run _previewDownRun = new() { Text = "↓ 24.5 MB/s" };
    private readonly Run _previewSpacerRun = new() { Text = "  " };
    private readonly Run _previewUpRun = new() { Text = "↑ 4.2 MB/s" };
    private bool _loading = true;

    public SettingsWindow(AppSettings settings, IReadOnlyList<NetworkAdapterInfo> adapters, HistoryStore history, Action? onSettingsSaved = null)
    {
        _settings = settings;
        _adapters = adapters;
        _history = history;
        _onSettingsSaved = onSettingsSaved;

        InitializeComponent();
        Title = "Settings";
        SetWindowIcon();
        SystemBackdrop = new MicaBackdrop();
        ((FrameworkElement)Content).RequestedTheme = ElementTheme.Light;
        AppWindow.Resize(new SizeInt32(640, 740));

        HudPreviewText.Inlines.Add(_previewDownRun);
        HudPreviewText.Inlines.Add(_previewSpacerRun);
        HudPreviewText.Inlines.Add(_previewUpRun);

        LoadValues();
        WirePreviewEvents();
        _loading = false;
        UpdatePreview();
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

        PollIntervalBox.Value = Math.Clamp(_settings.PollIntervalMs, 250, 5000);
        HistoryRetentionBox.Value = Math.Clamp(_settings.HistoryRetentionDays, 0, 365);
        TrackPerAppCheck.IsChecked = _settings.TrackPerApp;
        ShowHudCheck.IsChecked = _settings.ShowTaskbarHud;
        ShowGraphCheck.IsChecked = _settings.ShowGraph;
        IncludeVirtualCheck.IsChecked = _settings.IncludeVirtualAdapters;
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;

        // HUD Dock
        var dock = (_settings.HudDock ?? "Bottom").Trim();
        HudDockCombo.SelectedIndex = dock switch
        {
            "Bottom-Left" => 1,
            "Bottom-Center" => 2,
            "Top" or "Top-Center" => 3,
            "Top-Left" => 4,
            "Top-Right" => 5,
            "Left" => 6,
            "Right" => 7,
            _ => 0 // Bottom / Bottom-Right
        };
        HudOffsetXBox.Value = Math.Clamp(_settings.HudOffsetX, -1000, 1000);
        HudOffsetYBox.Value = Math.Clamp(_settings.HudOffsetY, -1000, 1000);

        // HUD Font
        HudFontFamilyCombo.Items.Clear();
        var fonts = new[] { "Segoe UI", "Segoe UI Variable Text", "Cascadia Code", "Consolas", "Bahnschrift", "Arial", "Calibri", "Lucida Console" };
        foreach (var f in fonts) HudFontFamilyCombo.Items.Add(f);
        HudFontFamilyCombo.Text = string.IsNullOrWhiteSpace(_settings.HudFontFamily) ? "Segoe UI" : _settings.HudFontFamily;

        HudFontSizeBox.Value = Math.Clamp(_settings.HudFontSize, 8, 24);
        HudFontWeightCombo.SelectedIndex = (_settings.HudFontWeight ?? "Normal").Trim().ToLowerInvariant() switch
        {
            "bold" => 2,
            "semibold" => 1,
            _ => 0
        };

        // HUD Colors
        HudColorSchemeCombo.SelectedIndex = (_settings.HudColorScheme ?? "Auto").Trim().ToLowerInvariant() switch
        {
            "cyan" or "cyberpunk cyan" => 1,
            "green" or "matrix green" => 2,
            "amber" or "neon amber" => 3,
            "violet" or "electric violet" => 4,
            "white" or "pure white" => 5,
            "custom" => 6,
            _ => 0
        };

        HudTextColorBox.Text = _settings.HudTextColor ?? string.Empty;
        HudDownColorBox.Text = _settings.HudDownColor ?? string.Empty;
        HudUpColorBox.Text = _settings.HudUpColor ?? string.Empty;
        HudBgColorBox.Text = _settings.HudBgColor ?? string.Empty;
        HudBgOpacityBox.Value = Math.Clamp((int)Math.Round(_settings.HudBgOpacity * 100), 0, 100);
        HudCornerRadiusBox.Value = Math.Clamp(_settings.HudCornerRadius, 0, 20);

        HudCompactCheck.IsChecked = _settings.HudCompact;
        HudShowBorderCheck.IsChecked = _settings.HudShowBorder;

        DailyLimitBox.Value = Math.Clamp(_settings.DailyLimitGB, 0, 9999);
        StatusText.Text = $"Saved to {SettingsService.SettingsFilePath}";
    }

    private void WirePreviewEvents()
    {
        HudFontFamilyCombo.SelectionChanged += (_, _) => UpdatePreview();
        HudFontFamilyCombo.TextSubmitted += (_, _) => UpdatePreview();
        HudFontFamilyCombo.LostFocus += (_, _) => UpdatePreview();
        HudFontFamilyCombo.Loaded += (_, _) =>
        {
            if (HudFontFamilyCombo.FindVisualChild<TextBox>() is { } fontTb)
                fontTb.TextChanged += (_, _) => UpdatePreview();
        };

        HudFontSizeBox.ValueChanged += (_, _) => UpdatePreview();
        HudFontWeightCombo.SelectionChanged += (_, _) => UpdatePreview();

        HudColorSchemeCombo.SelectionChanged += (_, _) =>
        {
            if (!_loading)
            {
                var tag = (HudColorSchemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                switch (tag?.ToLowerInvariant())
                {
                    case "cyan":
                        HudTextColorBox.Text = "#00E5FF";
                        HudDownColorBox.Text = "#38BDF8";
                        HudUpColorBox.Text = "#00E5FF";
                        HudBgColorBox.Text = "#0F172A";
                        HudBgOpacityBox.Value = 80;
                        break;
                    case "green":
                        HudTextColorBox.Text = "#00FF66";
                        HudDownColorBox.Text = "#4ADE80";
                        HudUpColorBox.Text = "#00FF66";
                        HudBgColorBox.Text = "#0A0F0D";
                        HudBgOpacityBox.Value = 80;
                        break;
                    case "amber":
                        HudTextColorBox.Text = "#FFB300";
                        HudDownColorBox.Text = "#FCD34D";
                        HudUpColorBox.Text = "#F59E0B";
                        HudBgColorBox.Text = "#1C1917";
                        HudBgOpacityBox.Value = 80;
                        break;
                    case "violet":
                        HudTextColorBox.Text = "#C084FC";
                        HudDownColorBox.Text = "#A855F7";
                        HudUpColorBox.Text = "#E879F9";
                        HudBgColorBox.Text = "#18181B";
                        HudBgOpacityBox.Value = 80;
                        break;
                    case "white":
                        HudTextColorBox.Text = "#FFFFFF";
                        HudDownColorBox.Text = "#FFFFFF";
                        HudUpColorBox.Text = "#E2E8F0";
                        HudBgColorBox.Text = "#000000";
                        HudBgOpacityBox.Value = 70;
                        break;
                    case "auto":
                        HudTextColorBox.Text = "";
                        HudDownColorBox.Text = "";
                        HudUpColorBox.Text = "";
                        HudBgColorBox.Text = "";
                        HudBgOpacityBox.Value = 75;
                        break;
                }
            }
            UpdatePreview();
        };

        HudTextColorBox.TextChanged += (_, _) => UpdatePreview();
        HudDownColorBox.TextChanged += (_, _) => UpdatePreview();
        HudUpColorBox.TextChanged += (_, _) => UpdatePreview();
        HudBgColorBox.TextChanged += (_, _) => UpdatePreview();
        HudBgOpacityBox.ValueChanged += (_, _) => UpdatePreview();
        HudCornerRadiusBox.ValueChanged += (_, _) => UpdatePreview();
        HudCompactCheck.Click += (_, _) => UpdatePreview();
        HudShowBorderCheck.Click += (_, _) => UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_loading) return;

        try
        {
            var fontName = string.IsNullOrWhiteSpace(HudFontFamilyCombo.Text) ? "Segoe UI" : HudFontFamilyCombo.Text.Trim();
            HudPreviewText.FontFamily = new FontFamily(fontName);

            var fs = Math.Clamp(HudFontSizeBox.Value, 8, 24);
            HudPreviewText.FontSize = fs;

            var fw = HudFontWeightCombo.SelectedIndex switch
            {
                2 => FontWeights.Bold,
                1 => FontWeights.SemiBold,
                _ => FontWeights.Normal
            };
            HudPreviewText.FontWeight = fw;

            var isCompact = HudCompactCheck.IsChecked == true;
            if (isCompact)
            {
                _previewDownRun.Text = "↓24.5M";
                _previewSpacerRun.Text = " ";
                _previewUpRun.Text = "↑4.2M";
            }
            else
            {
                _previewDownRun.Text = "↓ 24.5 MB/s";
                _previewSpacerRun.Text = "  ";
                _previewUpRun.Text = "↑ 4.2 MB/s";
            }

            var scheme = (HudColorSchemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Auto";
            var defaultText = Colors.White;
            var defaultBg = AppColorHelper.ParseColor("#0F172A", Colors.Black);

            Windows.UI.Color textCol;
            Windows.UI.Color downCol;
            Windows.UI.Color upCol;
            Windows.UI.Color bgCol;
            double opacity = Math.Clamp(HudBgOpacityBox.Value / 100.0, 0.0, 1.0);

            if (!string.IsNullOrWhiteSpace(HudTextColorBox.Text) ||
                !string.IsNullOrWhiteSpace(HudDownColorBox.Text) ||
                !string.IsNullOrWhiteSpace(HudUpColorBox.Text) ||
                !string.IsNullOrWhiteSpace(HudBgColorBox.Text))
            {
                textCol = AppColorHelper.ParseColor(HudTextColorBox.Text, defaultText);
                downCol = string.IsNullOrWhiteSpace(HudDownColorBox.Text) ? textCol : AppColorHelper.ParseColor(HudDownColorBox.Text, textCol);
                upCol = string.IsNullOrWhiteSpace(HudUpColorBox.Text) ? textCol : AppColorHelper.ParseColor(HudUpColorBox.Text, textCol);
                bgCol = string.IsNullOrWhiteSpace(HudBgColorBox.Text) ? defaultBg : AppColorHelper.ParseColor(HudBgColorBox.Text, defaultBg);
            }
            else
            {
                switch (scheme.ToLowerInvariant())
                {
                    case "cyan":
                        textCol = AppColorHelper.ParseColor("#00E5FF", Colors.Cyan);
                        downCol = AppColorHelper.ParseColor("#38BDF8", Colors.Cyan);
                        upCol = AppColorHelper.ParseColor("#00E5FF", Colors.Cyan);
                        bgCol = AppColorHelper.ParseColor("#0F172A", Colors.Black);
                        break;
                    case "green":
                        textCol = AppColorHelper.ParseColor("#00FF66", Colors.Green);
                        downCol = AppColorHelper.ParseColor("#4ADE80", Colors.Green);
                        upCol = AppColorHelper.ParseColor("#00FF66", Colors.Green);
                        bgCol = AppColorHelper.ParseColor("#0A0F0D", Colors.Black);
                        break;
                    case "amber":
                        textCol = AppColorHelper.ParseColor("#FFB300", Colors.Orange);
                        downCol = AppColorHelper.ParseColor("#FCD34D", Colors.Orange);
                        upCol = AppColorHelper.ParseColor("#F59E0B", Colors.Orange);
                        bgCol = AppColorHelper.ParseColor("#1C1917", Colors.Black);
                        break;
                    case "violet":
                        textCol = AppColorHelper.ParseColor("#C084FC", Colors.Purple);
                        downCol = AppColorHelper.ParseColor("#A855F7", Colors.Purple);
                        upCol = AppColorHelper.ParseColor("#E879F9", Colors.Purple);
                        bgCol = AppColorHelper.ParseColor("#18181B", Colors.Black);
                        break;
                    case "white":
                        textCol = Colors.White;
                        downCol = Colors.White;
                        upCol = AppColorHelper.ParseColor("#E2E8F0", Colors.White);
                        bgCol = Colors.Black;
                        break;
                    default: // Auto
                        textCol = defaultText;
                        downCol = defaultText;
                        upCol = defaultText;
                        bgCol = defaultBg;
                        break;
                }
            }

            if (opacity <= 0.001)
            {
                HudPreviewBorder.Background = new SolidColorBrush(Colors.Transparent);
            }
            else
            {
                HudPreviewBorder.Background = new SolidColorBrush(bgCol) { Opacity = opacity };
            }

            _previewDownRun.Foreground = new SolidColorBrush(downCol);
            _previewUpRun.Foreground = new SolidColorBrush(upCol);
            _previewSpacerRun.Foreground = new SolidColorBrush(textCol);

            var cr = Math.Clamp(HudCornerRadiusBox.Value, 0, 20);
            HudPreviewBorder.CornerRadius = new CornerRadius(cr);

            if (HudShowBorderCheck.IsChecked == true)
            {
                HudPreviewBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 255, 255, 255));
                HudPreviewBorder.BorderThickness = new Thickness(1);
            }
            else
            {
                HudPreviewBorder.BorderBrush = null;
                HudPreviewBorder.BorderThickness = new Thickness(0);
            }
        }
        catch { }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var savedOffset = PageScroll.VerticalOffset;

        var selected = AdapterModeCombo.Text?.Trim();
        if (string.IsNullOrWhiteSpace(selected) && AdapterModeCombo.SelectedItem is string s)
            selected = s.Trim();
        _settings.AdapterMode = string.IsNullOrWhiteSpace(selected) ? "Auto" : selected!;
        if (string.Equals(_settings.AdapterMode, "auto", StringComparison.OrdinalIgnoreCase)) _settings.AdapterMode = "Auto";
        if (string.Equals(_settings.AdapterMode, "combined", StringComparison.OrdinalIgnoreCase)) _settings.AdapterMode = "Combined";

        var interval = (int)Math.Round(PollIntervalBox.Value);
        _settings.PollIntervalMs = Math.Clamp(interval, 250, 5000);
        _settings.HistoryRetentionDays = Math.Clamp((int)Math.Round(HistoryRetentionBox.Value), 0, 365);
        _settings.TrackPerApp = TrackPerAppCheck.IsChecked == true;
        _settings.ShowTaskbarHud = ShowHudCheck.IsChecked == true;
        _settings.ShowGraph = ShowGraphCheck.IsChecked == true;
        _settings.IncludeVirtualAdapters = IncludeVirtualCheck.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;

        // HUD Dock
        _settings.HudDock = HudDockCombo.SelectedIndex switch
        {
            1 => "Bottom-Left",
            2 => "Bottom-Center",
            3 => "Top",
            4 => "Top-Left",
            5 => "Top-Right",
            6 => "Left",
            7 => "Right",
            _ => "Bottom"
        };
        _settings.HudOffsetX = Math.Clamp((int)Math.Round(HudOffsetXBox.Value), -1000, 1000);
        _settings.HudOffsetY = Math.Clamp((int)Math.Round(HudOffsetYBox.Value), -1000, 1000);

        // HUD Font
        var chosenFont = HudFontFamilyCombo.Text?.Trim();
        _settings.HudFontFamily = string.IsNullOrWhiteSpace(chosenFont) ? "Segoe UI" : chosenFont;
        _settings.HudFontSize = Math.Clamp(HudFontSizeBox.Value, 8, 24);
        _settings.HudFontWeight = HudFontWeightCombo.SelectedIndex switch
        {
            2 => "Bold",
            1 => "SemiBold",
            _ => "Normal"
        };

        // HUD Colors & Styles
        _settings.HudColorScheme = (HudColorSchemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Auto";
        _settings.HudTextColor = HudTextColorBox.Text?.Trim() ?? string.Empty;
        _settings.HudDownColor = HudDownColorBox.Text?.Trim() ?? string.Empty;
        _settings.HudUpColor = HudUpColorBox.Text?.Trim() ?? string.Empty;
        _settings.HudBgColor = HudBgColorBox.Text?.Trim() ?? string.Empty;
        _settings.HudBgOpacity = Math.Clamp(HudBgOpacityBox.Value / 100.0, 0.0, 1.0);
        _settings.HudCornerRadius = Math.Clamp(HudCornerRadiusBox.Value, 0, 20);
        _settings.HudCompact = HudCompactCheck.IsChecked == true;
        _settings.HudShowBorder = HudShowBorderCheck.IsChecked == true;

        var newLimit = Math.Clamp(Math.Round(DailyLimitBox.Value, 2), 0, 9999);
        if (newLimit != _settings.DailyLimitGB)
        {
            _settings.DailyLimitGB = newLimit;
            _settings.DailyAlertStage = 0;
        }
        _settings.ThemeMode = ThemeCombo.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        _settings.DarkMode = _settings.ThemeMode == "Dark";

        _store.Save(_settings);
        _store.ApplyStartWithWindows(_settings.StartWithWindows);
        _history.Prune(_settings.HistoryRetentionDays);

        _onSettingsSaved?.Invoke();

        StatusText.Text = "Saved. HUD customizations applied immediately; adapter mode applies immediately.";
        PageScroll.ChangeView(null, savedOffset, null, disableAnimation: true);
    }

    private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _history.Save();
            var rows = _history.ExportRows();
            if (rows.Count == 0)
            {
                StatusText.Text = "Nothing to export yet — history is empty.";
                return;
            }

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Windows11NetworkMonitor", "exports");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"usage-history-{DateTime.Now:yyyy-MM-dd_HHmmss}.csv");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Type,Date,App,DownloadBytes,UploadBytes");
            foreach (var r in rows)
            {
                sb.Append(CsvField(r.Type)).Append(',')
                  .Append(CsvField(r.Date)).Append(',')
                  .Append(CsvField(r.App)).Append(',')
                  .Append(r.DownloadBytes).Append(',')
                  .Append(r.UploadBytes).AppendLine();
            }
            System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            StatusText.Text = $"Exported {rows.Count} rows → {path}";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
        }
    }

    private static string CsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && (trimmed[0] == '=' || trimmed[0] == '+' || trimmed[0] == '-' || trimmed[0] == '@' || trimmed[0] == '\t' || trimmed[0] == '\r'))
        {
            value = "'" + value;
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
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

internal static class VisualTreeHelperExtensions
{
    public static T? FindVisualChild<T>(this DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var sub = FindVisualChild<T>(child);
            if (sub != null) return sub;
        }
        return null;
    }
}
