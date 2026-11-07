using System.Text.Json;
using Windows11NetworkMonitor.Models;

namespace Windows11NetworkMonitor.Services;

public sealed class SettingsService
{
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Windows11NetworkMonitor");
    private static readonly string SettingsPath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (!System.IO.File.Exists(SettingsPath)) return new AppSettings();
            var json = System.IO.File.ReadAllText(SettingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            loaded.ResetDailyIfNeeded();
            return loaded;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Dir);
        System.IO.File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
    }

    public void ApplyStartWithWindows(bool enable)
    {
        var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key is null) return;
        if (enable)
            key.SetValue("Windows11NetworkMonitor", Environment.ProcessPath ?? string.Empty);
        else
            key.DeleteValue("Windows11NetworkMonitor", false);
    }

    public static string SettingsFilePath => SettingsPath;
}
