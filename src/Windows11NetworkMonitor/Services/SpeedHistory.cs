using Windows11NetworkMonitor.Models;

namespace Windows11NetworkMonitor.Services;

/// <summary>
/// Fixed-size ring buffer of speed samples for the graph. Capacity is chosen at
/// startup to hold ~60 s of samples (60 000 ms / poll interval, clamped 30-240),
/// so the "last minute" window stays true even when the poll interval changes.
/// </summary>
public sealed class SpeedHistory
{
    private readonly Queue<SpeedSample> _samples = new();
    private readonly int _capacity;
    // Writes happen on the poll timer thread, while the UI thread reads the
    // samples for rendering — internal non-thread-safe collections need a lock.
    private readonly object _sync = new();

    public IReadOnlyList<SpeedSample> Samples { get { lock (_sync) return _samples.ToArray(); } }
    public double PeakDownloadBps { get { lock (_sync) return _peakDownloadBps; } }
    public double PeakUploadBps { get { lock (_sync) return _peakUploadBps; } }
    public double AvgDownloadBps { get { lock (_sync) return _avgDownloadBps; } }
    public double AvgUploadBps { get { lock (_sync) return _avgUploadBps; } }

    private double _peakDownloadBps;
    private double _peakUploadBps;
    private double _avgDownloadBps;
    private double _avgUploadBps;

    public SpeedHistory(int capacity = 60) => _capacity = Math.Max(1, capacity);

    public void Add(SpeedSample sample)
    {
        lock (_sync)
        {
            _samples.Enqueue(sample);
            while (_samples.Count > _capacity) _samples.Dequeue();

            _peakDownloadBps = Math.Max(_peakDownloadBps, sample.DownloadBps);
            _peakUploadBps = Math.Max(_peakUploadBps, sample.UploadBps);
            if (_samples.Count > 0)
            {
                _avgDownloadBps = _samples.Average(s => s.DownloadBps);
                _avgUploadBps = _samples.Average(s => s.UploadBps);
            }
        }
    }

    public void ResetPeaks()
    {
        lock (_sync)
        {
            _peakDownloadBps = 0;
            _peakUploadBps = 0;
        }
    }
}

public static class SpeedFormatter
{
    public static string Format(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:F0} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024:F1} KB/s";
        if (bytesPerSecond < 1024 * 1024 * 1024) return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
        return $"{bytesPerSecond / (1024L * 1024 * 1024):F2} GB/s";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
