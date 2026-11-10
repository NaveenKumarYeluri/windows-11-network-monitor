using System.Runtime.InteropServices;

namespace Windows11NetworkMonitor.Services;

/// <summary>Latency probe (ICMP ping) + current WiFi connection details via wlanapi.dll.</summary>
public static class WifiService
{
    /// <summary>Simple ICMP ping latency in ms, or -1 on failure.</summary>
    public static Task<long> PingAsync(string host = "1.1.1.1", int timeoutMs = 1000)
        => Task.Run(() =>
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = ping.Send(host, timeoutMs);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success ? reply.RoundtripTime : -1;
            }
            catch { return -1L; }
        });

    /// <summary>Current WiFi association: SSID + signal quality 0-100, or null when offline/no wifi.</summary>
    public sealed record WifiConnection(string Ssid, int SignalQuality);

    private const int WLAN_INTF_OPCODE_CURRENT_CONNECTION = 0x0B; // wlan_intf_opcode_current_connection
    private static readonly int InterfaceInfoSize = 16 /* GUID */ + 256 * 2 /* strInterfaceDescription */ + 4 /* isState */;
    private static readonly int ConnAttrSize = Marshal.SizeOf<WLAN_CONNECTION_ATTRIBUTES>();

    /// <summary>
    /// WlanOpenHandle → WlanEnumInterfaces → WlanQueryInterface(CURRENT_CONNECTION).
    /// Returns the first interface that is actually associated; null when none,
    /// on any API failure, or on wired-only systems (the API only covers WiFi).
    /// </summary>
    public static WifiConnection? QueryConnection()
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0 || handle == IntPtr.Zero) return null;
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out var listPtr) != 0 || listPtr == IntPtr.Zero) return null;
            try
            {
                var count = Marshal.ReadInt32(listPtr);
                for (var i = 0; i < count; i++)
                {
                    var info = listPtr + 8 + i * InterfaceInfoSize;
                    var guid = Marshal.PtrToStructure<Guid>(info);
                    if (WlanQueryInterface(handle, ref guid, WLAN_INTF_OPCODE_CURRENT_CONNECTION, IntPtr.Zero, out var size, out var dataPtr, IntPtr.Zero) != 0
                        || dataPtr == IntPtr.Zero || size < ConnAttrSize)
                    {
                        // Not associated, unsupported opcode on this interface, etc.
                        if (dataPtr != IntPtr.Zero) WlanFreeMemory(dataPtr);
                        continue;
                    }
                    try
                    {
                        var attr = Marshal.PtrToStructure<WLAN_CONNECTION_ATTRIBUTES>(dataPtr);
                        var assoc = attr.wlanAssociationAttributes;
                        if (assoc.dot11Ssid.uSSIDLength == 0) return null; // visible handle, not connected
                        return new WifiConnection(assoc.dot11Ssid.ucSSID, (int)assoc.wlanSignalQuality);
                    }
                    finally
                    {
                        WlanFreeMemory(dataPtr);
                    }
                }
                return null;
            }
            finally
            {
                WlanFreeMemory(listPtr);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    // --- wlanapi interop ---

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

    [DllImport("wlanapi.dll")]
    private static extern int WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, int wlanIntfOpcode, IntPtr pReserved, out int pdwDataSize, out IntPtr ppData, IntPtr pWlanOpcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr pMemory);

    // Matches wlanapi.h layouts for x64. SSID is max 32 chars + length field.
    [StructLayout(LayoutKind.Sequential)]
    private struct DOT11_SSID
    {
        public uint uSSIDLength;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ucSSID;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_ASSOCIATION_ATTRIBUTES
    {
        public DOT11_SSID dot11Ssid;
        public uint dot11BssType;
        public uint dot11PhyType;
        public uint wlanSignalQuality; // 0-100
        public uint ulRxRate;
        public uint ulTxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_SECURITY_ATTRIBUTES
    {
        public int bSecurityEnabled;
        public int bOneXEnabled;
        public uint dot11AuthAlgorithm;
        public uint dot11CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_CONNECTION_ATTRIBUTES
    {
        public uint InterfaceType;
        public int isState;
        public uint wlanConnectionMode;
        public IntPtr strProfileName;
        public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
        public WLAN_SECURITY_ATTRIBUTES wlanSecurityAttributes;
    }
}
