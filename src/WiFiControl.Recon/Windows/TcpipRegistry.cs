using Microsoft.Win32;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Windows;

// Перенесено из NetSwitch

/// <summary>Значения IPv4 адаптера в том виде, в каком они лежат в реестре.</summary>
internal sealed record TcpipInterfaceValues
{
    public int? EnableDhcp { get; init; }
    public int? DisableDhcpOnConnect { get; init; }
    public string[] IpAddress { get; init; } = [];
    public string[] SubnetMask { get; init; } = [];
    public string[] DefaultGateway { get; init; } = [];
    public string NameServer { get; init; } = "";
}

/// <summary>
/// Настроенное на адаптере хранится в реестре и доступно всегда. WMI и сетевой стек отдают
/// настройки только пока адаптер включён и есть соединение.
/// </summary>
internal static class TcpipRegistry
{
    public const string InterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    public static TcpipInterfaceValues? Read(Guid adapterId)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(InterfacesKey + "\\" + adapterId.ToString("B"));
        if (key == null) return null;

        return new TcpipInterfaceValues
        {
            EnableDhcp = key.GetValue("EnableDHCP") as int?,
            DisableDhcpOnConnect = key.GetValue("DisableDhcpOnConnect") as int?,
            IpAddress = Strings(key.GetValue("IPAddress")),
            SubnetMask = Strings(key.GetValue("SubnetMask")),
            DefaultGateway = Strings(key.GetValue("DefaultGateway")),
            NameServer = key.GetValue("NameServer") as string ?? ""
        };
    }

    /// <summary>Настроенное на адаптере; у адаптера без раздела в реестре всё автоматически.</summary>
    public static Ipv4Settings ReadSettings(Guid adapterId, AdapterState state) => ToSettings(Read(adapterId) ?? new TcpipInterfaceValues(), state);

    /// <param name="state">Без соединения признак «автоматически» в реестре не окончательный.</param>
    public static Ipv4Settings ToSettings(TcpipInterfaceValues values, AdapterState state)
    {
        // Без соединения Windows записывает ручные настройки, но EnableDHCP не сбрасывает
        // до подключения: о том, что адрес задан вручную, говорит только DisableDhcpOnConnect.
        // Флагу верим и без адреса: при подключении Windows по нему выключит DHCP
        bool manualPending = state != AdapterState.Connected && values.DisableDhcpOnConnect == 1;

        // Без явного запрета Windows получает адрес автоматически
        bool autoAddress = values.EnableDhcp != 0 && !manualPending;
        string address = "", mask = "", gateway = "";

        if (!autoAddress)
        {
            int first = Array.FindIndex(values.IpAddress, a => !IsBlank(a));
            if (first >= 0)
            {
                address = values.IpAddress[first];
                mask = first < values.SubnetMask.Length ? values.SubnetMask[first] : "";
            }
            gateway = values.DefaultGateway.FirstOrDefault(g => !IsBlank(g)) ?? "";
        }

        // При адресе вручную Windows не берёт DNS автоматически: пустой список значит «DNS нет»
        string[] dns = Split(values.NameServer);
        return new Ipv4Settings(autoAddress, address, mask, gateway, autoAddress && dns.Length == 0, dns);
    }

    internal static bool IsBlank(string address) => address.Trim().Length == 0 || address.Trim() == "0.0.0.0";

    // Windows разных версий пишет списки то многострочным значением, то одной строкой
    private static string[] Strings(object? value) => value switch
    {
        string[] many => many,
        string one => Split(one),
        _ => []
    };

    private static string[] Split(string list) => list.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
}
