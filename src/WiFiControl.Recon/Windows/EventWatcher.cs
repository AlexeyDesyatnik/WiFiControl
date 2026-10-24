using System.Net.NetworkInformation;
using ManagedNativeWifi;
using Microsoft.Win32;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Windows;

/// <summary>
/// Подписки на события компьютера. Каждая подписка ставится отдельно: отказ одной приходит событием,
/// остальные работают. Освобождение снимает все подписки.
/// </summary>
internal sealed class EventWatcher : IDisposable
{
    private readonly Action<SystemEvent> _onEvent;
    private readonly WlanNotifications? _wlan;
    private readonly bool _addresses;
    private readonly bool _power;
    private volatile bool _disposed;

    public EventWatcher(Action<SystemEvent> onEvent)
    {
        _onEvent = onEvent;

        try
        {
            _wlan = new WlanNotifications(Raise);
        }
        catch (Exception ex)
        {
            Raise(new EventFailure("Подписка на уведомления WLAN", ex));
        }

        try
        {
            NetworkChange.NetworkAddressChanged += OnAddressChanged;
            _addresses = true;
        }
        catch (Exception ex)
        {
            Raise(new EventFailure("Подписка на смену адресов", ex));
        }

        try
        {
            // Без цикла сообщений в потоке подписки SystemEvents заводит для этого собственный поток
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _power = true;
        }
        catch (Exception ex)
        {
            Raise(new EventFailure("Подписка на сон и выход из сна", ex));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _wlan?.Dispose();
        if (_addresses) NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        if (_power) SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void Raise(SystemEvent systemEvent)
    {
        // Событие, которое уже было в пути, когда подписки снимали, не записывается
        if (!_disposed) _onEvent(systemEvent);
    }

    private void OnAddressChanged(object? sender, EventArgs e)
    {
        SystemEvent addresses;
        try
        {
            addresses = new AddressesChanged(ReadWifiAddresses());
        }
        catch (Exception ex)
        {
            addresses = new EventFailure("Смена адресов: чтение адресов Wi-Fi-адаптеров", ex);
        }
        Raise(addresses);
    }

    private static List<AdapterAddresses> ReadWifiAddresses()
    {
        // Wi-Fi-адаптеры — те, что видит WLAN API: у виртуальных адаптеров Wi-Fi Direct тот же тип в сетевом стеке
        var wifi = NativeWifi.EnumerateInterfaces().Select(adapter => adapter.Id).ToHashSet();

        var addresses = new List<AdapterAddresses>();
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!Guid.TryParse(adapter.Id, out Guid id) || !wifi.Contains(id)) continue;
            // Читается при любом состоянии: переход от 169.254.x.x к адресу по DHCP — то, ради чего лента и ведётся
            addresses.Add(new AdapterAddresses(id, adapter.Name, adapter.OperationalStatus.ToString(), AdapterReader.ReadActive(adapter)));
        }
        return addresses;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        Raise(new PowerModeChanged(e.Mode.ToString()));
    }
}
