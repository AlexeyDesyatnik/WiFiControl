using System.Globalization;
using System.Management;

namespace WiFiControl.Core.Windows;

// Перенесено из NetSwitch

/// <summary>
/// Включает DHCP в постоянных настройках адаптера через MSFT_NetIPInterface (то же делает Set-NetIPInterface).
/// Только так снимается DisableDhcpOnConnect у адаптера без соединения: netsh считает, что DHCP уже включён,
/// а Win32_NetworkAdapterConfiguration без соединения не работает. См. ADR 0003.
/// </summary>
internal static class DhcpSwitch
{
    private const string PolicyStore = "PolicyStore";
    private const string PersistentStore = "PersistentStore";

    // MSFT_NetIPInterface.AddressFamily и .Dhcp
    private const int IPv4 = 2;
    private const uint DhcpEnabled = 1;

    /// <summary>Убеждается, что постоянные настройки IPv4 адаптера доступны для записи, ничего не меняя.</summary>
    public static void EnsureAvailable(string adapterId, string adapterName)
    {
        Persistent(adapterId, adapterName, _ => { });
    }

    public static void EnablePersistent(string adapterId, string adapterName)
    {
        var put = new PutOptions { Type = PutType.UpdateOnly };
        put.Context.Add(PolicyStore, PersistentStore);

        Persistent(adapterId, adapterName, item =>
        {
            item["Dhcp"] = DhcpEnabled;
            item.Put(put);
        });
    }

    private static void Persistent(string adapterId, string adapterName, Action<ManagementObject> action)
    {
        int? index = null;
        foreach (WmiRow row in Wmi.Query("SELECT InterfaceIndex FROM Win32_NetworkAdapter WHERE GUID = '" + AdapterReader.QueryId(adapterId) + "'"))
        {
            index = row.Number("InterfaceIndex");
        }
        if (index == null) throw new InvalidOperationException("Адаптер «" + adapterName + "» не найден.");

        // Без контекста запрос отдаёт действующие настройки, где DHCP и так включён, и запись ничего не меняет
        var enumeration = new System.Management.EnumerationOptions();
        enumeration.Context.Add(PolicyStore, PersistentStore);
        string wql = "SELECT * FROM MSFT_NetIPInterface WHERE InterfaceIndex = "
            + index.Value.ToString(CultureInfo.InvariantCulture) + " AND AddressFamily = " + IPv4.ToString(CultureInfo.InvariantCulture);

        bool found = false;
        using (var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\StandardCimv2"), new ObjectQuery(wql), enumeration))
        using (ManagementObjectCollection items = searcher.Get())
        {
            foreach (ManagementObject item in items.Cast<ManagementObject>())
            {
                using (item)
                {
                    found = true;
                    action(item);
                }
            }
        }
        if (!found) throw new InvalidOperationException("У адаптера «" + adapterName + "» не найдены постоянные настройки IPv4.");
    }
}
