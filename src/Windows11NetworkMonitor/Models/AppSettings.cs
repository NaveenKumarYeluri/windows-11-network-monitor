using CommunityToolkit.Mvvm.ComponentModel;

namespace Windows11NetworkMonitor.Models;

public sealed partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private string adapterMode = "Auto"; // Auto | Combined | <adapterId>

    [ObservableProperty]
    private int pollIntervalMs = 1000;

    [ObservableProperty]
    private bool showTaskbarHud = true;

    [ObservableProperty]
    private bool showGraph = true;

    [ObservableProperty]
    private bool includeVirtualAdapters = false;

    [ObservableProperty]
    private bool startWithWindows = false;

    [ObservableProperty]
    private bool darkMode = false;

    [ObservableProperty]
    private string themeMode = "System"; // System | Light | Dark

    [ObservableProperty]
    private int historyRetentionDays = 90;

    [ObservableProperty]
    private bool trackPerApp = true;

    // Pause semantics: stop accumulating session/daily/history/per-app counters only.
    // Live speed display, graph, tray and HUD keep updating so the link stays visible.
    [ObservableProperty]
    private bool trackingPaused = false;

    // Daily usage alert. 0 = disabled; the limit counts download + upload combined.
    [ObservableProperty]
    private double dailyLimitGB = 0;    // Alert bookkeeping (persisted so restarts don't re-nag): highest threshold
    // announced (0 none, 1 = 90%, 2 = 100%) and the day it was announced for.
    [ObservableProperty]
    private int dailyAlertStage;

    [ObservableProperty]
    private DateOnly dailyAlertDate = DateOnly.FromDateTime(DateTime.Now);

    // HUD placement (relative / absolute offsets in DIPs)
    [ObservableProperty]
    private int hudOffsetX = 0;

    [ObservableProperty]
    private int hudOffsetY = 0;

    [ObservableProperty]
    private string hudDock = "Bottom"; // Bottom | Bottom-Left | Bottom-Center | Top | Top-Left | Top-Right | Left | Right

    // HUD appearance & customisations
    [ObservableProperty]
    private string hudFontFamily = "Segoe UI";

    [ObservableProperty]
    private double hudFontSize = 11;

    [ObservableProperty]
    private string hudFontWeight = "Normal"; // Normal | SemiBold | Bold

    [ObservableProperty]
    private string hudColorScheme = "Auto"; // Auto | Cyan | Green | Amber | Violet | White | Custom

    [ObservableProperty]
    private string hudTextColor = ""; // Hex (e.g. #FFFFFF) or empty for theme/auto

    [ObservableProperty]
    private string hudDownColor = ""; // Hex for download speed (e.g. #38BDF8) or empty

    [ObservableProperty]
    private string hudUpColor = ""; // Hex for upload speed (e.g. #4ADE80) or empty

    [ObservableProperty]
    private string hudBgColor = ""; // Hex for background (e.g. #000000) or empty for auto

    [ObservableProperty]
    private double hudBgOpacity = 0.75; // 0.0 (transparent) to 1.0 (opaque)

    [ObservableProperty]
    private double hudCornerRadius = 6; // 0 to 20 DIPs

    [ObservableProperty]
    private bool hudShowBorder = false;

    [ObservableProperty]
    private string hudBorderColor = ""; // Hex or empty

    [ObservableProperty]
    private bool hudCompact = false; // Compact format: ↓12M ↑2M

    [ObservableProperty]
    private long sessionDownloadBytes;

    [ObservableProperty]
    private long sessionUploadBytes;

    [ObservableProperty]
    private long dailyDownloadBytes;

    [ObservableProperty]
    private long dailyUploadBytes;

    [ObservableProperty]
    private DateOnly dailyDate = DateOnly.FromDateTime(DateTime.Now);

    public void ResetDailyIfNeeded()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (DailyDate != today)
        {
            DailyDate = today;
            DailyDownloadBytes = 0;
            DailyUploadBytes = 0;
        }
    }
}
