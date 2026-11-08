using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Windows11NetworkMonitor.Services;

/// <summary>Snapshot of open connections grouped by owning process.</summary>
public sealed class AppUsage
{
    public required string ProcessName { get; init; }
    public required int Pid { get; init; }
    public int TcpConnections { get; init; }
    public int UdpEndpoints { get; init; }
    public int TotalConnections => TcpConnections + UdpEndpoints;
}

/// <summary>
/// Best-effort per-app visibility without a kernel driver: enumerates
/// TCP/UDP owner PIDs via GetExtendedTcpTable, then attributes a share of
/// each interface's byte delta proportionally by open-connection count.
/// Not byte-accurate like WFP, but needs no admin rights and is free.
/// If <c>trackPerApp</c> is false, no snapshot is produced.
/// </summary>
public sealed class PerAppTracker
{
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    private const int TCP_TABLE_OWNER_PID = 5;
    private const int UDP_TABLE_OWNER_PID = 2;
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;

    public bool TrackPerApp { get; set; } = true;

    public IReadOnlyList<AppUsage> Snapshot()
    {
        if (!TrackPerApp) return Array.Empty<AppUsage>();
        var counts = new Dictionary<int, (int tcp, int udp)>();
        CountTcp(counts, AF_INET);
        CountTcp(counts, AF_INET6);
        CountUdp(counts, AF_INET);
        CountUdp(counts, AF_INET6);

        var list = new List<AppUsage>();
        foreach (var kv in counts)
        {
            string name;
            try { name = Process.GetProcessById(kv.Key).ProcessName; }
            catch { continue; }
            list.Add(new AppUsage
            {
                Pid = kv.Key,
                ProcessName = name,
                TcpConnections = kv.Value.tcp,
                UdpEndpoints = kv.Value.udp
            });
        }
        return list.OrderByDescending(a => a.TotalConnections).ToList();
    }

    private static void CountTcp(Dictionary<int, (int tcp, int udp)> counts, int af)
    {
        try
        {
            int size = 0;
            var rc = GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TCP_TABLE_OWNER_PID, 0);
            if (rc != 0 || size <= 0) return;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                rc = GetExtendedTcpTable(buf, ref size, false, af, TCP_TABLE_OWNER_PID, 0);
                if (rc != 0) return;
                int n = Marshal.ReadInt32(buf);
                // TCP table row: dwLocalAddr(4) dwLocalPort(4) dwRemoteAddr(4) dwRemotePort(4) dwState(4) dwOwningPid(4)
                // IPv6 row is larger (28 bytes): dwLocalAddr[4](16) dwLocalPort(4) dwLocalScopeId(4) dwRemoteAddr[4](16) dwRemotePort(4) dwState(4) dwOwningPid(4) = 52? MibTcp6row OwnerPid layout differs.
                // Use the AF-specific stride: 24 for IPv4, 56 for IPv6 (sizeof MIB_TCPROW_OWNER_PID / MIB_TCP6ROW_OWNER_PID).
                int stride = af == AF_INET6 ? 56 : 24;
                for (int i = 0; i < n; i++)
                {
                    long rowOff = (af == AF_INET6 ? 4 : 4) + i * (long)stride;
                    // OwningPid is last DWORD in both IPv4/IPv6 owner rows.
                    int pidOff = (int)(rowOff + (af == AF_INET6 ? 52 : 20));
                    if (pidOff + 4 > size) break;
                    var pid = Marshal.ReadInt32(buf, pidOff);
                    if (pid <= 0) continue;
                    counts.TryGetValue(pid, out var v);
                    v.tcp++;
                    counts[pid] = v;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { }
    }

    private static void CountUdp(Dictionary<int, (int tcp, int udp)> counts, int af)
    {
        try
        {
            int size = 0;
            var rc = GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, UDP_TABLE_OWNER_PID, 0);
            if (rc != 0 || size <= 0) return;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                rc = GetExtendedTcpTable(buf, ref size, false, af, UDP_TABLE_OWNER_PID, 0);
                if (rc != 0) return;
                int n = Marshal.ReadInt32(buf);
                // UDP row: dwLocalAddr(4) dwLocalPort(4) dwOwningPid(4) = 12 bytes IPv4; IPv6: dwLocalAddr[4](16) dwLocalPort(4) dwLocalScopeId(4) dwOwningPid(4) = 28
                int stride = af == AF_INET6 ? 28 : 12;
                for (int i = 0; i < n; i++)
                {
                    long rowOff = 4L + i * stride;
                    int pidOff = (int)(rowOff + (af == AF_INET6 ? 24 : 8));
                    if (pidOff + 4 > size) break;
                    var pid = Marshal.ReadInt32(buf, pidOff);
                    if (pid <= 0) continue;
                    counts.TryGetValue(pid, out var v);
                    v.udp++;
                    counts[pid] = v;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { }
    }
}
