using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Windows;

public sealed class WindowsNetworkSystem : INetworkSystem
{
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
}
