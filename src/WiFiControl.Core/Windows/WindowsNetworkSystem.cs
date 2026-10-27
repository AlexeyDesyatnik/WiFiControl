using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Windows;

public sealed class WindowsNetworkSystem : INetworkSystem
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SettlePoll = TimeSpan.FromMilliseconds(300);

    public Task<IReadOnlyList<Adapter>> ReadAdaptersAsync(CancellationToken cancellationToken)
    {
        // WMI и реестр отвечают синхронно и иногда долго, поэтому чтение уходит с вызывающего потока
        return Task.Run<IReadOnlyList<Adapter>>(() =>
        {
            try
            {
                return AdapterReader.ReadAll(cancellationToken);
            }
            catch (Exception e) when (e is ManagementException or COMException or NetworkInformationException
                                          or SecurityException or UnauthorizedAccessException or IOException
                                          // Свойство WMI с неожиданным типом или значением
                                          or FormatException or InvalidCastException or OverflowException)
            {
                throw new InvalidOperationException("Windows не отдала список сетевых адаптеров: " + e.Message, e);
            }
        }, cancellationToken);
    }

    public async Task WriteIpv4Async(Adapter adapter, Ipv4Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            // Выключенного адаптера нет в сетевом стеке, и netsh ему ничего не запишет
            if (await CurrentStateAsync(adapter, cancellationToken).ConfigureAwait(false) == AdapterState.Disabled)
            {
                await AdapterSwitch.EnableAsync(adapter.Id, adapter.Name, cancellationToken).ConfigureAwait(false);
            }

            var failures = new List<string>();
            Ipv4Settings current = await ReadConfiguredAsync(adapter, cancellationToken).ConfigureAwait(false);
            await RunAllAsync(Netsh.Ipv4Commands(adapter.Name, current, settings), failures, cancellationToken).ConfigureAwait(false);

            // Без соединения «source=dhcp» не снимает ждущие подключения ручные настройки: netsh отвечает,
            // что DHCP уже включён. Тогда стираем ручные адреса и включаем DHCP в постоянных настройках (ADR 0003).
            if (settings.AutoAddress
                && await CurrentStateAsync(adapter, cancellationToken).ConfigureAwait(false) != AdapterState.Connected
                && !(await ReadConfiguredAsync(adapter, cancellationToken).ConfigureAwait(false)).AutoAddress)
            {
                await ReturnToDhcpAsync(adapter, failures, cancellationToken).ConfigureAwait(false);
            }

            // netsh иногда сообщает об ошибке, сделав дело (например, «DHCP уже включён»), и наоборот.
            // Поэтому верим не коду возврата, а тому, что в итоге записано на адаптере.
            if (await SettlesAsync(adapter, settings, cancellationToken).ConfigureAwait(false)) return;

            throw new InvalidOperationException(failures.Count > 0
                ? string.Join(" ", failures)
                : "Windows приняла команды, но настройки адаптера не совпали с заданными.");
        }
        catch (Exception e) when (e is ManagementException or COMException or SecurityException
                                      or UnauthorizedAccessException or IOException
                                      // Свойство WMI с неожиданным типом или значением, идентификатор адаптера не GUID
                                      or FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException("Windows не дала записать настройки IPv4 адаптера «" + adapter.Name + "»: " + e.Message, e);
        }
    }

    private static async Task ReturnToDhcpAsync(Adapter adapter, List<string> failures, CancellationToken cancellationToken)
    {
        try
        {
            // Без MSFT_NetIPInterface флаг не снять, а стёртый адрес оставил бы адаптер
            // совсем без адреса после подключения. Поэтому сначала убеждаемся, что включить DHCP получится.
            await Task.Run(() => DhcpSwitch.EnsureAvailable(adapter.Id, adapter.Name), cancellationToken).ConfigureAwait(false);

            TcpipInterfaceValues recorded = await Task.Run(() => TcpipRegistry.Read(adapter.Id), cancellationToken).ConfigureAwait(false)
                ?? new TcpipInterfaceValues();
            // Если адрес или шлюз не стёрлись, включённый DHCP скрыл бы их от проверки после записи
            if (await RunAllAsync(Netsh.DeleteAddressCommands(adapter.Name, recorded), failures, cancellationToken).ConfigureAwait(false))
            {
                await Task.Run(() => DhcpSwitch.EnablePersistent(adapter.Id, adapter.Name), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException e)
        {
            failures.Add(e.Message);
        }
        catch (Exception e) when (e is ManagementException or COMException)
        {
            failures.Add("Windows не смогла включить DHCP на адаптере «" + adapter.Name + "»: " + e.Message);
        }
    }

    /// <summary>
    /// Только что включённый адаптер подключается несколько секунд, и всё это время Windows переписывает
    /// его настройки в реестре (NetSwitch #9). Поэтому несовпадение перепроверяем, пока оно не устоится.
    /// </summary>
    private static async Task<bool> SettlesAsync(Adapter adapter, Ipv4Settings settings, CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + SettleTimeout;
        while (!(await ReadConfiguredAsync(adapter, cancellationToken).ConfigureAwait(false)).SameAs(settings))
        {
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(SettlePoll, cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    /// <returns>true, если все команды выполнились успешно.</returns>
    private static async Task<bool> RunAllAsync(IReadOnlyList<string> commands, List<string> failures, CancellationToken cancellationToken)
    {
        int before = failures.Count;
        foreach (string command in commands)
        {
            string? failure = await Netsh.RunAsync(command, cancellationToken).ConfigureAwait(false);
            if (failure != null) failures.Add(failure);
        }
        return failures.Count == before;
    }

    // Прочитанное раньше состояние могло устареть: адаптер только что включили или к нему подключились
    private static Task<AdapterState> CurrentStateAsync(Adapter adapter, CancellationToken cancellationToken)
    {
        return Task.Run(() => AdapterReader.ReadState(adapter.Id) ?? adapter.State, cancellationToken);
    }

    private static Task<Ipv4Settings> ReadConfiguredAsync(Adapter adapter, CancellationToken cancellationToken)
    {
        return Task.Run(() => TcpipRegistry.ReadSettings(adapter.Id, AdapterReader.ReadState(adapter.Id) ?? adapter.State), cancellationToken);
    }
}
