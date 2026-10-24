using System.Net.NetworkInformation;
using System.Net.Sockets;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Windows;

// Перенесено из NetSwitch

internal static class AdapterReader
{
    public const string AdaptersQuery = "SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL";

    // Win32_NetworkAdapter.ConfigManagerErrorCode
    private const int DeviceDisabled = 22;

    public static IReadOnlyList<Adapter> ReadAll()
    {
        // Id у адаптеров стека — GUID в фигурных скобках
        var interfaces = new Dictionary<Guid, NetworkInterface>();
        foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (Guid.TryParse(item.Id, out Guid id)) interfaces[id] = item;
        }

        var adapters = new List<Adapter>();
        foreach (WmiRow row in Wmi.Query(AdaptersQuery))
        {
            Adapter adapter = FromWmi(row);
            adapter = adapter with { Configured = TcpipRegistry.ReadSettings(adapter.Id, adapter.State) };

            if (interfaces.TryGetValue(adapter.Id, out NetworkInterface? active))
            {
                adapter = adapter with
                {
                    IsWireless = active.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                    // Без соединения стек продолжает отдавать самоназначенный адрес 169.254.x.x и старые DNS
                    Active = adapter.State == AdapterState.Connected ? ReadActive(active) : null
                };
            }

            adapters.Add(adapter);
        }
        return adapters;
    }

    internal static Adapter FromWmi(WmiRow row)
    {
        string pnpDeviceId = row.Text("PNPDeviceID");
        return new Adapter(
            Guid.TryParse(row.Text("GUID"), out Guid id) ? id : Guid.Empty,
            row.Text("NetConnectionID"),
            row.Text("Name"),
            pnpDeviceId,
            row.Text("MACAddress"),
            AdapterClassifier.IsPhysical(pnpDeviceId),
            StateFrom(row.Number("NetConnectionStatus"), row.Number("ConfigManagerErrorCode")));
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

    internal static ActiveIpv4 ReadActive(NetworkInterface item)
    {
        var addresses = new List<string>();
        var gateways = new List<string>();
        var dns = new List<string>();
        bool hasConflict = false;
        IPInterfaceProperties properties = item.GetIPProperties();

        foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
        {
            if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;

            if (unicast.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Duplicate)
            {
                hasConflict = true;
                continue;
            }

            addresses.Add(Ipv4.TryMaskToPrefix(unicast.IPv4Mask?.ToString(), out int prefix)
                ? unicast.Address + "/" + prefix
                : unicast.Address.ToString());
        }
        foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
        {
            if (gateway.Address.AddressFamily != AddressFamily.InterNetwork) continue;
            if (gateway.Address.ToString() == "0.0.0.0") continue;

            gateways.Add(gateway.Address.ToString());
        }
        foreach (System.Net.IPAddress server in properties.DnsAddresses)
        {
            if (server.AddressFamily == AddressFamily.InterNetwork) dns.Add(server.ToString());
        }
        return new ActiveIpv4(addresses, gateways, dns, hasConflict);
    }
}
