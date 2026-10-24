using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Principal;
using ManagedNativeWifi;
using Microsoft.Win32;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Windows;

/// <summary>Настоящий компьютер. Только читает.</summary>
internal sealed class WindowsSystem : IReadOnlySystem
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const int ErrorInvalidState = 5023;

    static WindowsSystem()
    {
        // Без этого ManagedNativeWifi сводит часть отказов WLAN API к ActionResult и теряет код ошибки Windows.
        // Настройка общая на весь процесс; разведка пользуется библиотекой только отсюда.
        NativeWifi.ThrowsOnAnyFailure = true;
    }

    public string ComputerName => Environment.MachineName;

    public WindowsVersion ReadWindows()
    {
        // Явно 64-разрядное представление: 32-разрядный процесс иначе прочитал бы копию для WOW64
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
        using RegistryKey key = machine.OpenSubKey(CurrentVersionKey)
            ?? throw new InvalidOperationException("Нет раздела реестра HKLM\\" + CurrentVersionKey + ".");

        return new WindowsVersion(
            key.GetValue("ProductName") as string ?? "",
            key.GetValue("EditionID") as string,
            key.GetValue("DisplayVersion") as string,
            int.Parse(key.GetValue("CurrentBuildNumber") as string ?? "0", System.Globalization.CultureInfo.InvariantCulture),
            key.GetValue("UBR") is int ubr ? ubr : 0);
    }

    public ProcessIdentity ReadProcess()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        // При включённом UAC роль администратора есть только у повышенного маркера
        return new ProcessIdentity(identity.Name, new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
    }

    public LaunchOrigin ReadLaunch()
    {
        return new LaunchOrigin(ReadParent(), ServiceProcessId("Schedule"));
    }

    public IReadOnlyList<WifiAdapter> ReadWifiAdapters()
    {
        return NativeWifi.EnumerateInterfaces()
            .Select(adapter => new WifiAdapter(adapter.Id, adapter.Description, adapter.State.ToString()))
            .ToList();
    }

    public IReadOnlyList<NetworkConnection> ReadNetworkConnections()
    {
        var connections = new List<NetworkConnection>();
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Id у адаптеров Windows — GUID в фигурных скобках, тот же, что у Wi-Fi-адаптера в WLAN API
            if (Guid.TryParse(adapter.Id, out Guid id))
            {
                connections.Add(new NetworkConnection(id, adapter.Name, adapter.Description, adapter.OperationalStatus.ToString()));
            }
        }
        return connections;
    }

    public WifiConnection? ReadCurrentConnection(Guid adapter)
    {
        (ActionResult result, CurrentConnectionInfo connection) value;
        try
        {
            value = NativeWifi.GetCurrentConnection(adapter);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorInvalidState)
        {
            // Так WlanQueryInterface отвечает за адаптер без подключения
            return null;
        }

        // Коды Windows бросаются исключениями (ThrowsOnAnyFailure), так что здесь остаётся только отказ самой обёртки
        if (value.result != ActionResult.Success)
            throw new InvalidOperationException("ManagedNativeWifi не разобрал текущее подключение: тип BSS, проверка подлинности или шифр ему не известны (" + value.result + ").");

        CurrentConnectionInfo connection = value.connection;
        return new WifiConnection(
            connection.Ssid.ToString(),
            FormatBssid(connection.Bssid),
            connection.ProfileName,
            connection.IsSecurityEnabled,
            connection.AuthenticationAlgorithm.ToString(),
            connection.CipherAlgorithm.ToString(),
            connection.SignalQuality,
            connection.ConnectionMode.ToString());
    }

    public ConnectedBss? ReadConnectedBss(Guid adapter, string bssid)
    {
        (ActionResult result, IEnumerable<BssNetworkInfo> list) = NativeWifi.EnumerateBssNetworks(adapter);
        if (result != ActionResult.Success)
            throw new InvalidOperationException("ManagedNativeWifi не вернул список BSS (" + result + ").");

        BssNetworkInfo? bss = list.FirstOrDefault(entry => FormatBssid(entry.Bssid) == bssid);
        return bss == null ? null : new ConnectedBss(bss.Frequency, bss.Band, bss.Channel, bss.Rssi);
    }

    public IReadOnlyList<Adapter> ReadAdapters() => AdapterReader.ReadAll();

    public InteractiveUser ReadInteractiveUser() => ProxyReader.ReadInteractiveUser();

    public SystemProxyState ReadSystemProxy(string userSid) => ProxyReader.ReadSystemProxy(userSid);

    public ProxyPolicy ReadProxyPolicy(string userSid) => ProxyReader.ReadPolicy(userSid);

    public ProxySettings ReadWinHttpProxy() => ProxyReader.ReadWinHttp();

    public IReadOnlyList<IpInterface> ReadIpInterfaces() => IpHelper.ReadInterfaces();

    public IReadOnlyList<Route> ReadDefaultRoutes() => IpHelper.ReadDefaultRoutes();

    public BestRoute? ReadBestRoute(string destination) => IpHelper.ReadBestRoute(destination);

    private static string FormatBssid(NetworkIdentifier bssid) => BitConverter.ToString(bssid.ToBytes());

    private static ParentProcess ReadParent()
    {
        using Process current = Process.GetCurrentProcess();
        int parentId = NativeMethods.ParentProcessId(current.Handle);

        try
        {
            using Process parent = Process.GetProcessById(parentId);
            // Номер завершившегося родителя мог достаться процессу, запущенному позже нас
            if (StartedAfter(parent, current)) return new ParentProcess(parentId, null);
            return new ParentProcess(parentId, parent.ProcessName);
        }
        catch (ArgumentException)
        {
            return new ParentProcess(parentId, null);
        }
    }

    private static bool StartedAfter(Process parent, Process current)
    {
        try
        {
            return parent.StartTime > current.StartTime;
        }
        catch (Win32Exception)
        {
            // Время запуска процесса службы без прав администратора не прочитать; тогда верим номеру
            return false;
        }
    }

    /// <returns>null, если служба не запущена.</returns>
    private static int? ServiceProcessId(string service)
    {
        int id = NativeMethods.ServiceProcessId(service);
        return id == 0 ? null : id;
    }
}
