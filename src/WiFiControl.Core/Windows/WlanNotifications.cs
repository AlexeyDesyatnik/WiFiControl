using System.ComponentModel;
using System.Runtime.InteropServices;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Windows;

/// <summary>
/// Подписка на уведомления WLAN собственным P/Invoke: ManagedNativeWifi отдаёт только переведённые события,
/// без кода причины и без конца сканирования (docs/adr/0001). Перенесено из разведочной сборки.
/// </summary>
internal sealed class WlanNotifications : IDisposable
{
    private const int SourceNone = 0;
    private const int SourceAcm = 0x08;
    private const uint ClientVersion = 2;

    private readonly Action<WifiNotification> _onNotification;
    // Делегат держится в поле: иначе сборщик мусора уберёт его, пока Windows ещё зовёт обратный вызов
    private readonly WlanNotificationCallback _callback;
    private IntPtr _client;

    public WlanNotifications(Action<WifiNotification> onNotification)
    {
        _onNotification = onNotification;
        _callback = OnNotification;

        Check(WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out _client), "WlanOpenHandle");
        int status = WlanRegisterNotification(_client, SourceAcm, false, _callback, IntPtr.Zero, IntPtr.Zero, out _);
        if (status != 0)
        {
            WlanCloseHandle(_client, IntPtr.Zero);
            _client = IntPtr.Zero;
            Check(status, "WlanRegisterNotification");
        }
    }

    public void Dispose()
    {
        if (_client == IntPtr.Zero) return;
        // Снятие подписки дожидается обратных вызовов, которые уже идут, поэтому из самого обратного вызова его не зовут
        WlanRegisterNotification(_client, SourceNone, false, null, IntPtr.Zero, IntPtr.Zero, out _);
        WlanCloseHandle(_client, IntPtr.Zero);
        _client = IntPtr.Zero;
    }

    private void OnNotification(IntPtr data, IntPtr context)
    {
        NotificationData notification = Marshal.PtrToStructure<NotificationData>(data);
        if (Read(notification.Source, notification.Code, notification.InterfaceGuid, notification.Data, notification.DataSize) is { } read)
            _onNotification(read);
    }

    /// <summary>Разбирает данные уведомления по разметке из wlanapi.h; null — уведомление инструменту не нужно.</summary>
    internal static WifiNotification? Read(int source, int code, Guid adapter, IntPtr data, int size)
    {
        if (source != SourceAcm) return null;
        if (code is AcmScanComplete or AcmScanFail) return new WifiScanCompleted(adapter);
        if (code is not (AcmConnectionComplete or AcmConnectionAttemptFail or AcmDisconnected) || size < ConnectionDataSize) return null;

        string network = Network(data);
        int reason = Marshal.ReadInt32(data + ConnectionReasonOffset);
        return code switch
        {
            AcmDisconnected => new WifiDisconnected(adapter, network),
            // connection_complete приходит и после неудачи — тогда с ненулевым кодом причины (docs/connect-check.md)
            AcmConnectionComplete when reason == 0 => new WifiConnected(adapter, network),
            _ => new WifiConnectionFailed(adapter, network, reason),
        };
    }

    // WLAN_CONNECTION_NOTIFICATION_DATA: WLAN_CONNECTION_MODE, WCHAR strProfileName[256],
    // DOT11_SSID { ULONG uSSIDLength; UCHAR ucSSID[32]; }, DOT11_BSS_TYPE, BOOL bSecurityEnabled, WLAN_REASON_CODE, …
    private const int SsidLengthOffset = 516;
    private const int SsidOffset = 520;
    private const int SsidMaxLength = 32;
    private const int ConnectionReasonOffset = 560;
    private const int ConnectionDataSize = 564;

    private static string Network(IntPtr data)
    {
        int length = Math.Clamp(Marshal.ReadInt32(data + SsidLengthOffset), 0, SsidMaxLength);
        var bytes = new byte[length];
        Marshal.Copy(data + SsidOffset, bytes, 0, length);
        return NetworkKey.FromBytes(bytes);
    }

    // wlan_notification_acm_* из wlanapi.h
    private const int AcmScanComplete = 7;
    private const int AcmScanFail = 8;
    private const int AcmConnectionComplete = 10;
    private const int AcmConnectionAttemptFail = 11;
    private const int AcmDisconnected = 21;

    private static void Check(int status, string call)
    {
        if (status != 0) throw new Win32Exception(status, call + ": " + new Win32Exception(status).Message);
    }

    /// <summary>WLAN_NOTIFICATION_DATA.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NotificationData
    {
        public int Source;
        public int Code;
        public Guid InterfaceGuid;
        public int DataSize;
        public IntPtr Data;
    }

    private delegate void WlanNotificationCallback(IntPtr data, IntPtr context);

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanRegisterNotification(IntPtr clientHandle, int notificationSource, bool ignoreDuplicate,
        WlanNotificationCallback? callback, IntPtr context, IntPtr reserved, out int previousNotificationSource);
}
