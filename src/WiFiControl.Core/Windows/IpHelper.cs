using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Windows;

/// <summary>Маршруты IPv4 через IP Helper (iphlpapi.dll). Все вызовы только читают. Перенесено из разведочной сборки.</summary>
internal static class IpHelper
{
    private const ushort AfInet = 2;

    // GetBestRoute2: к адресу нет ни одного маршрута
    private const int ErrorNetworkUnreachable = 1231;

    // У таблиц IP Helper строки начинаются после NumEntries, выровненного по 8 байтам: строки содержат NET_LUID
    private const int TableHeaderSize = 8;

    public static IReadOnlyList<DefaultRoute> ReadDefaultRoutes()
    {
        Check(GetIpForwardTable2(AfInet, out IntPtr table), "GetIpForwardTable2");
        try
        {
            var routes = new List<DefaultRoute>();
            foreach (IpForwardRow row in Rows<IpForwardRow>(table))
            {
                if (row.DestinationPrefix.PrefixLength == 0) routes.Add(new DefaultRoute(AdapterId(row.InterfaceLuid), Ipv4Text(row.NextHop)));
            }
            return routes;
        }
        finally
        {
            FreeMibTable(table);
        }
    }

    /// <summary>Адаптер маршрута, который Windows выбрала бы для адреса; null, если маршрута нет.</summary>
    public static Guid? ReadBestRouteAdapter(IPAddress destination)
    {
        var target = new SockAddrInet { Family = AfInet, Address = BitConverter.ToUInt32(destination.GetAddressBytes()) };
        // Выбирает маршрут по таблице, ничего не отправляя
        int status = GetBestRoute2(IntPtr.Zero, 0, IntPtr.Zero, ref target, 0, out IpForwardRow row, out _);
        if (status == ErrorNetworkUnreachable) return null;
        Check(status, "GetBestRoute2(" + destination + ")");

        return AdapterId(row.InterfaceLuid);
    }

    private static Guid AdapterId(ulong luid)
    {
        Check(ConvertInterfaceLuidToGuid(ref luid, out Guid id), "ConvertInterfaceLuidToGuid");
        return id;
    }

    private static string Ipv4Text(SockAddrInet address) => new IPAddress(address.Address).ToString();

    private static IEnumerable<T> Rows<T>(IntPtr table) where T : struct
    {
        int count = Marshal.ReadInt32(table);
        int size = Marshal.SizeOf<T>();
        for (int i = 0; i < count; i++)
        {
            yield return Marshal.PtrToStructure<T>(table + TableHeaderSize + i * size);
        }
    }

    // IP Helper возвращает код ошибки, а не выставляет GetLastError
    private static void Check(int status, string call)
    {
        if (status != 0) throw new Win32Exception(status, call + ": " + new Win32Exception(status).Message);
    }

    /// <summary>SOCKADDR_INET; из адреса IPv6 читаются только семейство и первые байты, нужен лишь IPv4.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SockAddrInet
    {
        public ushort Family;
        public ushort Port;
        /// <summary>sin_addr в порядке байтов сети.</summary>
        public uint Address;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] Rest;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IpAddressPrefix
    {
        public SockAddrInet Prefix;
        public byte PrefixLength;
    }

    /// <summary>MIB_IPFORWARD_ROW2.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IpForwardRow
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public IpAddressPrefix DestinationPrefix;
        public SockAddrInet NextHop;
        public byte SitePrefixLength;
        public uint ValidLifetime;
        public uint PreferredLifetime;
        public uint Metric;
        public int Protocol;
        public byte Loopback;
        public byte AutoconfigureAddress;
        public byte Publish;
        public byte Immortal;
        public uint Age;
        public int Origin;
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetIpForwardTable2(ushort family, out IntPtr table);

    [DllImport("iphlpapi.dll")]
    private static extern int GetBestRoute2(IntPtr interfaceLuid, uint interfaceIndex, IntPtr sourceAddress, ref SockAddrInet destinationAddress,
        uint addressSortOptions, out IpForwardRow bestRoute, out SockAddrInet bestSourceAddress);

    [DllImport("iphlpapi.dll")]
    private static extern int ConvertInterfaceLuidToGuid(ref ulong luid, out Guid guid);

    [DllImport("iphlpapi.dll")]
    private static extern void FreeMibTable(IntPtr memory);
}
