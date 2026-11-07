namespace Windows11NetworkMonitor.Models;

public sealed class NetworkAdapterInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsWiFi { get; init; }
    public bool IsEthernet { get; init; }
    public bool IsLoopback { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsUp { get; init; }
    public string? IPv4 { get; init; }
    public string? IPv6 { get; init; }
    public string? MacAddress { get; init; }
    public long BytesReceived { get; init; }
    public long BytesSent { get; init; }
    public long SpeedBps { get; init; }
}

public sealed class SpeedSample
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public double DownloadBps { get; init; }
    public double UploadBps { get; init; }
}
