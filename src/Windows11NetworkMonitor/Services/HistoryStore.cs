using System.Text.Json;

namespace Windows11NetworkMonitor.Services;

/// <summary>Daily usage history with user-controlled retention (default 90 days).</summary>
public sealed class HistoryStore
{
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Windows11NetworkMonitor");
    private static readonly string FilePath = Path.Combine(Dir, "usage-history.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private Dictionary<string, DayUsage> _data = new();

    public sealed class DayUsage
    {
        public long DownloadBytes { get; set; }
        public long UploadBytes { get; set; }
    }

    public void Load()
    {
        try
        {
            if (!System.IO.File.Exists(FilePath)) return;
            var json = System.IO.File.ReadAllText(FilePath);
            _data = JsonSerializer.Deserialize<Dictionary<string, DayUsage>>(json, JsonOpts) ?? new();
        }
        catch { _data = new(); }
    }

    public void AddDelta(long downDelta, long upDelta)
    {
        if (downDelta == 0 && upDelta == 0) return;
        var key = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        if (!_data.TryGetValue(key, out var day)) { day = new DayUsage(); _data[key] = day; }
        day.DownloadBytes += Math.Max(0, downDelta);
        day.UploadBytes += Math.Max(0, upDelta);
        Save();
    }

    public void Prune(int retentionDays)
    {
        if (retentionDays <= 0) retentionDays = 90;
        var cutoff = DateOnly.FromDateTime(DateTime.Now.AddDays(-retentionDays));
        var toRemove = _data.Keys.Where(k => DateOnly.TryParse(k, out var d) && d < cutoff).ToList();
        foreach (var k in toRemove) _data.Remove(k);
        Save();
    }

    public DayUsage For(DateOnly date) => _data.TryGetValue(date.ToString("yyyy-MM-dd"), out var d) ? d : new DayUsage();

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, JsonOpts));
        }
        catch { }
    }
}
