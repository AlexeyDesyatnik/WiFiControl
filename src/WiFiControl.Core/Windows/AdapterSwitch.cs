using System.Globalization;
using System.Management;

namespace WiFiControl.Core.Windows;

// Перенесено из NetSwitch

internal static class AdapterSwitch
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(300);

    /// <summary>Возвращается, когда адаптер появился в сетевом стеке: только тогда ему можно записывать настройки IPv4.</summary>
    public static async Task EnableAsync(string adapterId, string adapterName, CancellationToken cancellationToken)
    {
        await Task.Run(() => InvokeEnable(adapterId, adapterName), cancellationToken).ConfigureAwait(false);

        DateTime deadline = DateTime.UtcNow + Timeout;
        // Опрос сетевого стека тоже бывает долгим, поэтому и он уходит с вызывающего потока
        while (!await Task.Run(() => AdapterReader.IsInStack(adapterId), cancellationToken).ConfigureAwait(false))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException("Адаптер «" + adapterName + "» не успел включиться за "
                    + Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " секунд.");
            }
            await Task.Delay(Poll, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void InvokeEnable(string adapterId, string adapterName)
    {
        bool found = false;
        using var searcher = new ManagementObjectSearcher(@"root\cimv2",
            "SELECT * FROM Win32_NetworkAdapter WHERE GUID = '" + AdapterReader.QueryId(adapterId) + "'");
        using ManagementObjectCollection items = searcher.Get();
        foreach (ManagementObject item in items.Cast<ManagementObject>())
        {
            using (item)
            {
                found = true;
                int code = Convert.ToInt32(item.InvokeMethod("Enable", null), CultureInfo.InvariantCulture);
                if (code != 0)
                {
                    throw new InvalidOperationException("Windows не смогла включить адаптер «" + adapterName + "», код ошибки "
                        + code.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }
        }
        if (!found) throw new InvalidOperationException("Адаптер «" + adapterName + "» не найден.");
    }
}
