using System.Security;
using Microsoft.Win32;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Windows;

/// <summary>Читает записи реестра о MAC-адресе Wi-Fi-адаптера. Ничего не меняет, прав администратора не требует.</summary>
internal static class MacRegistryReader
{
    public static MacRegistry Read(Guid adapter)
    {
        // Явно 64-разрядное представление: 32-разрядный процесс иначе прочитал бы копию для WOW64
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

        string wlanSvcPath = MacRegistry.WlanSvcInterfaces + "\\" + adapter.ToString("B");
        using RegistryKey? wlanSvc = machine.OpenSubKey(wlanSvcPath);
        // Тип значения не известен заранее, поэтому оно берётся как есть
        object? randomMacState = wlanSvc?.GetValue("RandomMacState", null, RegistryValueOptions.DoNotExpandEnvironmentNames);

        (string? deviceKey, string? networkAddress) = ReadDevice(machine, adapter);
        return new MacRegistry(wlanSvc == null ? null : "HKLM\\" + wlanSvcPath, randomMacState, deviceKey, networkAddress);
    }

    private static (string? DeviceKey, string? NetworkAddress) ReadDevice(RegistryKey machine, Guid adapter)
    {
        using RegistryKey adapterClass = machine.OpenSubKey(MacRegistry.NetworkAdapterClass)
            ?? throw new InvalidOperationException("Нет раздела реестра HKLM\\" + MacRegistry.NetworkAdapterClass + ".");

        foreach (string name in adapterClass.GetSubKeyNames())
        {
            RegistryKey? device;
            try
            {
                device = adapterClass.OpenSubKey(name);
            }
            catch (SecurityException)
            {
                // Подраздел Properties без прав администратора не открыть; адаптеров в нём нет
                continue;
            }

            using (device)
            {
                if (device?.GetValue("NetCfgInstanceId") is not string id || !Guid.TryParse(id, out Guid instance) || instance != adapter) continue;
                return ("HKLM\\" + MacRegistry.NetworkAdapterClass + "\\" + name, device.GetValue("NetworkAddress") as string);
            }
        }
        return (null, null);
    }
}
