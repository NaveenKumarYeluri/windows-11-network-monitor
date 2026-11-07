using CommunityToolkit.Mvvm.ComponentModel;

namespace Windows11NetworkMonitor.Models;

public sealed partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private string adapterMode = "Auto"; // Auto | Combined | <adapterId>

    [ObservableProperty]
    private int pollIntervalMs = 1000;

    [ObservableProperty]
    private bool showTrayIcon = true;

    [ObservableProperty]
    private bool showTaskbarHud = true;

    [ObservableProperty]
    private bool showGraph = true;

    [ObservableProperty]
    private bool includeVirtualAdapters = false;

    [ObservableProperty]
    private bool startWithWindows = false;

    [ObservableProperty]
    private bool darkMode = true;

    [ObservableProperty]
    private int graphWindowSeconds = 60;

    [ObservableProperty]
    private string downloadColor = "#0078D4";

    [ObservableProperty]
    private string uploadColor = "#D83B01";

    // HUD placement (relative / absolute offsets in DIPs)
    [ObservableProperty]
    private int hudOffsetX = 0;

    [ObservableProperty]
    private int hudOffsetY = 0;

    [ObservableProperty]
    private string hudDock = "Bottom"; // Bottom | Top | Left | Right

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
        if (dailyDate != today)
        {
            dailyDate = today;
            dailyDownloadBytes = 0;
            dailyUploadBytes = 0;
        }
    }
}
