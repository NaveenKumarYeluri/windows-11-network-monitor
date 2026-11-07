using Windows11NetworkMonitor.Models;

namespace Windows11NetworkMonitor.Services;

/// <summary>Fixed-size ring buffer of speed samples for the graph (default 120 x 0.5s = 60s).</summary>
public sealed class SpeedHistory
{
    private readonly Queue<SpeedSample> _samples = new();
    private readonly int _capacity;

    public IReadOnlyList<SpeedSample> Samples => _samples.ToArray();
    public double PeakDownloadBps { get; private set; }
    public double PeakUploadBps { get; private set; }
    public double AvgDownloadBps { get; private set; }
    public double AvgUploadBps { get; private set; }

    public SpeedHistory(int capacity = 120) => _capacity = capacity;

    public void Add(SpeedSample sample)
    {
        _samples.Enqueue(sample);
        while (_samples.Count > _capacity) _samples.Dequeue();

        PeakDownloadBps = Math.Max(PeakDownloadBps, sample.DownloadBps);
        PeakUploadBps = Math.Max(PeakUploadBps, sample.UploadBps);
        if (_samples.Count > 0)
        {
            AvgDownloadBps = _samples.Average(s => s.DownloadBps);
            AvgUploadBps = _samples.Average(s => s.UploadBps);
        }
    }

    public void ResetPeaks()
    {
        PeakDownloadBps = 0;
        PeakUploadBps = 0;
    }
}

public static class SpeedFormatter
{
    public static string Format(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:F0} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024:F1} KB/s";
        if (bytesPerSecond < 1024 * 1024 * 1024) return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
        return $"{bytesPerSecond / (1024 * 1024 * 1024):F2} GB/s";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
