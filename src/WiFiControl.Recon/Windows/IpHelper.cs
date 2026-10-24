using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Windows;

/// <summary>Интерфейсы и маршруты IPv4 через IP Helper (iphlpapi.dll). Все вызовы только читают.</summary>
internal static class IpHelper
{
    private const ushort AfInet = 2;

    // GetBestRoute2: к адресу нет ни одного маршрута
    private const int ErrorNetworkUnreachable = 1231;

    // У таблиц IP Helper строки начинаются после NumEntries, выровненного по 8 байтам: строки содержат NET_LUID
    private const int TableHeaderSize = 8;

    public static IReadOnlyList<IpInterface> ReadInterfaces()
    {
        Check(GetIpInterfaceTable(AfInet, out IntPtr table), "GetIpInterfaceTable");
        try
        {
            var interfaces = new List<IpInterface>();
            foreach (IpInterfaceRow row in Rows<IpInterfaceRow>(table))
            {
                interfaces.Add(new IpInterface(AdapterId(row.InterfaceLuid), (int)row.InterfaceIndex, (int)row.Metric, row.UseAutomaticMetric != 0));
            }
            return interfaces;
        }
        finally
        {
            FreeMibTable(table);
        }
    }

    public static IReadOnlyList<Route> ReadDefaultRoutes()
    {
        Check(GetIpForwardTable2(AfInet, out IntPtr table), "GetIpForwardTable2");
        try
        {
            var routes = new List<Route>();
            foreach (IpForwardRow row in Rows<IpForwardRow>(table))
            {
                if (row.DestinationPrefix.PrefixLength == 0) routes.Add(RouteFrom(row));
            }
            return routes;
        }
        finally
        {
            FreeMibTable(table);
        }
    }

    public static BestRoute? ReadBestRoute(string destination)
    {
        IPAddress address = IPAddress.Parse(destination);
        if (address.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Нужен адрес IPv4: " + destination, nameof(destination));

        var target = new SockAddrInet { Family = AfInet, Address = BitConverter.ToUInt32(address.GetAddressBytes()) };
        // Выбирает маршрут по таблице, ничего не отправляя
        int status = GetBestRoute2(IntPtr.Zero, 0, IntPtr.Zero, ref target, 0, out IpForwardRow row, out SockAddrInet source);
        if (status == ErrorNetworkUnreachable) return null;
        Check(status, "GetBestRoute2(" + destination + ")");

        return new BestRoute(RouteFrom(row), Ipv4Text(source));
    }

    public static MacAddresses ReadMacAddresses(Guid adapter)
    {
        Check(ConvertInterfaceGuidToLuid(ref adapter, out ulong luid), "ConvertInterfaceGuidToLuid");

        // MIB_IF_ROW2 велика и полна строк фиксированной длины; нужные поля читаются по смещениям из netioapi.h
        IntPtr row = Marshal.AllocHGlobal(IfRow2Size);
        try
        {
            Marshal.Copy(new byte[IfRow2Size], 0, row, IfRow2Size);
            Marshal.WriteInt64(row, (long)luid);
            Check(GetIfEntry2(row), "GetIfEntry2");

            // Сверка с GUID в строке: если смещения разошлись с заголовками Windows, лучше отказ, чем чужие байты
            var rowGuid = Marshal.PtrToStructure<Guid>(row + IfRow2InterfaceGuid);
            if (rowGuid != adapter) throw new InvalidOperationException("GetIfEntry2 вернул строку другого интерфейса: " + rowGuid.ToString("B") + ".");

            int length = Marshal.ReadInt32(row + IfRow2PhysicalAddressLength);
            if (length is < 0 or > IfMaxPhysAddressLength) throw new InvalidOperationException("GetIfEntry2: длина MAC-адреса " + length + ".");
            return new MacAddresses(PhysicalAddress(row + IfRow2PhysicalAddress, length), PhysicalAddress(row + IfRow2PermanentPhysicalAddress, length));
        }
        finally
        {
            Marshal.FreeHGlobal(row);
        }
    }

    private static string PhysicalAddress(IntPtr address, int length)
    {
        var bytes = new byte[length];
        Marshal.Copy(address, bytes, 0, length);
        return BitConverter.ToString(bytes);
    }

    // MIB_IF_ROW2: NET_LUID, NET_IFINDEX, GUID, WCHAR Alias[257], WCHAR Description[257], ULONG PhysicalAddressLength,
    // UCHAR PhysicalAddress[32], UCHAR PermanentPhysicalAddress[32], …; всего 1352 байта
    private const int IfRow2Size = 1352;
    private const int IfRow2InterfaceGuid = 12;
    private const int IfRow2PhysicalAddressLength = 1056;
    private const int IfRow2PhysicalAddress = 1060;
    private const int IfRow2PermanentPhysicalAddress = 1092;
    private const int IfMaxPhysAddressLength = 32;

    private static Route RouteFrom(IpForwardRow row) =>
        new(AdapterId(row.InterfaceLuid), (int)row.InterfaceIndex,
            Ipv4Text(row.DestinationPrefix.Prefix) + "/" + row.DestinationPrefix.PrefixLength, Ipv4Text(row.NextHop), (int)row.Metric);

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

    /// <summary>MIB_IPINTERFACE_ROW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IpInterfaceRow
    {
        public ushort Family;
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public uint MaxReassemblySize;
        public ulong InterfaceIdentifier;
        public uint MinRouterAdvertisementInterval;
        public uint MaxRouterAdvertisementInterval;
        public byte AdvertisingEnabled;
        public byte ForwardingEnabled;
        public byte WeakHostSend;
        public byte WeakHostReceive;
        public byte UseAutomaticMetric;
        public byte UseNeighborUnreachabilityDetection;
        public byte ManagedAddressConfigurationSupported;
        public byte OtherStatefulConfigurationSupported;
        public byte AdvertiseDefaultRoute;
        public int RouterDiscoveryBehavior;
        public uint DadTransmits;
        public uint BaseReachableTime;
        public uint RetransmitTime;
        public uint PathMtuDiscoveryTimeout;
        public int LinkLocalAddressBehavior;
        public uint LinkLocalAddressTimeout;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public uint[] ZoneIndices;
        public uint SitePrefixLength;
        public uint Metric;
        public uint NlMtu;
        public byte Connected;
        public byte SupportsWakeUpPatterns;
        public byte SupportsNeighborDiscovery;
        public byte SupportsRouterDiscovery;
        public uint ReachableTime;
        public byte TransmitOffload;
        public byte ReceiveOffload;
        public byte DisableDefaultRoutes;
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetIpInterfaceTable(ushort family, out IntPtr table);

    [DllImport("iphlpapi.dll")]
    private static extern int GetIpForwardTable2(ushort family, out IntPtr table);

    [DllImport("iphlpapi.dll")]
    private static extern int GetBestRoute2(IntPtr interfaceLuid, uint interfaceIndex, IntPtr sourceAddress, ref SockAddrInet destinationAddress,
        uint addressSortOptions, out IpForwardRow bestRoute, out SockAddrInet bestSourceAddress);

    [DllImport("iphlpapi.dll")]
    private static extern int ConvertInterfaceLuidToGuid(ref ulong luid, out Guid guid);

    [DllImport("iphlpapi.dll")]
    private static extern int ConvertInterfaceGuidToLuid(ref Guid guid, out ulong luid);

    [DllImport("iphlpapi.dll")]
    private static extern int GetIfEntry2(IntPtr row);

    [DllImport("iphlpapi.dll")]
    private static extern void FreeMibTable(IntPtr memory);
}
