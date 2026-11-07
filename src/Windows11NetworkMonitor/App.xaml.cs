using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows11NetworkMonitor.Models;
using Windows11NetworkMonitor.Services;

namespace Windows11NetworkMonitor;

public partial class App : Application
{
    private static DispatcherQueue? _dispatcher;
    private static AppSettings? _settings;
    private static NetworkAdapterService? _adapterService;
    private static MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        var store = new SettingsService();
        _settings = store.Load();
        _adapterService = new NetworkAdapterService(_settings, TimeSpan.FromMilliseconds(_settings.PollIntervalMs));

        _mainWindow = new MainWindow(_settings, _adapterService);
        _mainWindow.Activate();
        _mainWindow.InitTray(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_mainWindow));

        _ = _adapterService.RunAsync();
    }

    public static DispatcherQueue? Dispatcher => _dispatcher;
    public static AppSettings? Settings => _settings;
}
