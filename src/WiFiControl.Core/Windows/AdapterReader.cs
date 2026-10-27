using System.Net.NetworkInformation;
using System.Net.Sockets;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Windows;

// Перенесено из NetSwitch

internal static class AdapterReader
{
    public const string AdaptersQuery = "SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL";

    // Win32_NetworkAdapter.ConfigManagerErrorCode
    private const int DeviceDisabled = 22;

    public static List<Adapter> ReadAll(CancellationToken cancellationToken)
    {
        Dictionary<string, NetworkInterface> interfaces = StackInterfaces();

        var adapters = new List<Adapter>();
        foreach (WmiRow row in Wmi.Query(AdaptersQuery))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Adapter adapter = FromWmi(row);
            adapter.Configured = TcpipRegistry.ReadSettings(adapter.Id, adapter.State);

            if (interfaces.TryGetValue(adapter.Id, out NetworkInterface? active))
            {
                adapter.IsWireless = active.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

                // Без соединения стек продолжает отдавать самоназначенный адрес 169.254.x.x и старые DNS
                if (adapter.State == AdapterState.Connected) adapter.Active = ReadActive(active);
            }

            adapters.Add(adapter);
        }
        return adapters;
    }

    private static Dictionary<string, NetworkInterface> StackInterfaces()
    {
        var interfaces = new Dictionary<string, NetworkInterface>(StringComparer.OrdinalIgnoreCase);
        foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
        {
            interfaces[item.Id] = item;
        }
        return interfaces;
    }

    internal static Adapter FromWmi(WmiRow row)
    {
        string pnpDeviceId = row.Text("PNPDeviceID");
        return new Adapter
        {
            Id = row.Text("GUID"),
            Name = row.Text("NetConnectionID"),
            Description = row.Text("Name"),
            PnpDeviceId = pnpDeviceId,
            MacAddress = row.Text("MACAddress"),
            IsPhysical = AdapterClassifier.IsPhysical(pnpDeviceId),
            State = StateFrom(row.Number("NetConnectionStatus"), row.Number("ConfigManagerErrorCode"))
        };
    }

    /// <param name="connectionStatus">Win32_NetworkAdapter.NetConnectionStatus</param>
    /// <param name="deviceError">Win32_NetworkAdapter.ConfigManagerErrorCode</param>
    internal static AdapterState StateFrom(int? connectionStatus, int? deviceError)
    {
        // Выключенный адаптер Windows 7 показывает как «устройство отсутствует» (4), а не как «отключён»
        if (deviceError == DeviceDisabled) return AdapterState.Disabled;

        return connectionStatus switch
        {
            2 => AdapterState.Connected,
            // Windows 7 сообщает о выдернутом кабеле и как 7, и как 0
            0 or 7 => AdapterState.NoLink,
            5 => AdapterState.Disabled,
            4 or 6 or null => AdapterState.Unavailable,
            // Подключение, отключение и стадии проверки подлинности Wi-Fi
            _ => AdapterState.Connecting
        };
    }

    private static ActiveIpv4 ReadActive(NetworkInterface item)
    {
        var result = new ActiveIpv4();
        IPInterfaceProperties properties = item.GetIPProperties();

        foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
        {
            if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;

            if (unicast.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Duplicate)
            {
                result.HasConflict = true;
                continue;
            }

            result.Addresses.Add(Ipv4.TryMaskToPrefix(unicast.IPv4Mask?.ToString(), out int prefix)
                ? unicast.Address + "/" + prefix
                : unicast.Address.ToString());
        }
        foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
        {
            if (gateway.Address.AddressFamily != AddressFamily.InterNetwork) continue;
            if (gateway.Address.ToString() == "0.0.0.0") continue;

            result.Gateways.Add(gateway.Address.ToString());
        }
        foreach (System.Net.IPAddress server in properties.DnsAddresses)
        {
            if (server.AddressFamily == AddressFamily.InterNetwork) result.Dns.Add(server.ToString());
        }
        return result;
    }
}
