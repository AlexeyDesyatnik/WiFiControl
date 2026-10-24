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

    public IReadOnlyList<BssEntry> ReadBssList(Guid adapter)
    {
        (ActionResult result, IEnumerable<BssNetworkInfo> list) = NativeWifi.EnumerateBssNetworks(adapter);
        if (result != ActionResult.Success)
            throw new InvalidOperationException("ManagedNativeWifi не вернул список BSS (" + result + ").");

        return list
            .Select(bss => new BssEntry(bss.Ssid.ToString(), FormatBssid(bss.Bssid), bss.Frequency, bss.Band, bss.Channel, bss.Rssi))
            .ToList();
    }

    public bool Scan(Guid adapter, TimeSpan timeout)
    {
        // Библиотека ждёт уведомления ACM о конце сканирования — успешном или неудачном, их она не различает.
        // Вызов синхронный: чтение и так идёт вне потока окна
        IEnumerable<Guid> scanned = NativeWifi.ScanNetworksAsync(ScanMode.OnlySpecified, [adapter], timeout, CancellationToken.None)
            .GetAwaiter().GetResult();
        return scanned.Contains(adapter);
    }

    public IReadOnlyList<AvailableNetwork> ReadAvailableNetworks(Guid adapter)
    {
        (ActionResult result, IEnumerable<AvailableNetworkInfo> list) = NativeWifi.EnumerateAvailableNetworks(adapter);
        if (result != ActionResult.Success)
            throw new InvalidOperationException("ManagedNativeWifi не вернул список сетей (" + result + ").");

        return list
            .Select(network => new AvailableNetwork(
                network.Ssid.ToString(),
                network.BssType.ToString(),
                network.IsConnectable,
                network.SignalQuality,
                network.IsSecurityEnabled,
                network.ProfileName,
                network.AuthenticationAlgorithm.ToString(),
                network.CipherAlgorithm.ToString()))
            .ToList();
    }

    public IReadOnlyList<WindowsProfile> ReadWindowsProfiles(Guid adapter)
    {
        // ManagedNativeWifi зовёт WlanGetProfile с нулевыми флагами, без WLAN_PROFILE_GET_PLAINTEXT_KEY:
        // ключ сети приходит зашифрованным, а дальше этого метода XML всё равно не уходит
        return NativeWifi.EnumerateProfiles()
            .Where(profile => profile.InterfaceInfo.Id == adapter)
            .Select(profile => WindowsProfileXml.Read(profile.Name, profile.ProfileType.ToString(), profile.Position, profile.Document.Xml))
            .ToList();
    }

    public MacAddresses ReadMacAddresses(Guid adapter) => IpHelper.ReadMacAddresses(adapter);

    public MacRegistry ReadMacRegistry(Guid adapter) => MacRegistryReader.Read(adapter);

    public IReadOnlyList<Adapter> ReadAdapters() => AdapterReader.ReadAll();

    public InteractiveUser ReadInteractiveUser() => ProxyReader.ReadInteractiveUser();

    public SystemProxyState ReadSystemProxy(string userSid) => ProxyReader.ReadSystemProxy(userSid);

    public ProxyPolicy ReadProxyPolicy(string userSid) => ProxyReader.ReadPolicy(userSid);

    public ProxySettings ReadWinHttpProxy() => ProxyReader.ReadWinHttp();

    public IReadOnlyList<IpInterface> ReadIpInterfaces() => IpHelper.ReadInterfaces();

    public IReadOnlyList<Route> ReadDefaultRoutes() => IpHelper.ReadDefaultRoutes();

    public BestRoute? ReadBestRoute(string destination) => IpHelper.ReadBestRoute(destination);

    public LocationConsent ReadUserLocationConsent(string userSid) => LocationReader.ReadUser(userSid);

    public LocationConsent ReadMachineLocationConsent() => LocationReader.ReadMachine();

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
