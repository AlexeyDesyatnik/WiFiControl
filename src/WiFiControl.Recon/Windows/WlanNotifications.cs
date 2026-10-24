using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Windows;

/// <summary>
/// Подписка на уведомления WLAN собственным P/Invoke: ManagedNativeWifi (<c>NativeWifiPlayer</c>) отдаёт только
/// переведённые события — без источника и кода уведомления и без конца сканирования. Только читает.
/// </summary>
internal sealed class WlanNotifications : IDisposable
{
    public const int SourceAcm = 0x08;
    public const int SourceMsm = 0x10;
    private const int SourceNone = 0;
    private const uint ClientVersion = 2;

    private readonly Action<WlanNotification> _onNotification;
    // Делегат держится в поле: иначе сборщик мусора уберёт его, пока Windows ещё зовёт обратный вызов
    private readonly WlanNotificationCallback _callback;
    private IntPtr _client;

    public WlanNotifications(Action<WlanNotification> onNotification)
    {
        _onNotification = onNotification;
        _callback = OnNotification;

        Check(WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out _client), "WlanOpenHandle");
        int status = WlanRegisterNotification(_client, SourceAcm | SourceMsm, false, _callback, IntPtr.Zero, IntPtr.Zero, out _);
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
        _onNotification(Read(notification.Source, notification.Code, notification.InterfaceGuid, notification.Data, notification.DataSize));
    }

    /// <summary>Разбирает данные уведомления по разметке из wlanapi.h.</summary>
    internal static WlanNotification Read(int source, int code, Guid adapter, IntPtr data, int size)
    {
        bool acm = source == SourceAcm;
        var notification = new WlanNotification(adapter, acm ? "ACM" : source == SourceMsm ? "MSM" : "0x" + source.ToString("X"), code, CodeName(source, code));

        if (acm && code is AcmConnectionStart or AcmConnectionComplete or AcmConnectionAttemptFail or AcmDisconnecting or AcmDisconnected
            && size >= ConnectionDataSize)
        {
            return WithReason(notification with { Ssid = Ssid(data), WindowsProfileName = ProfileName(data) }, Marshal.ReadInt32(data + ConnectionReasonOffset));
        }
        if (acm && code == AcmScanFail && size >= sizeof(int))
        {
            return WithReason(notification, Marshal.ReadInt32(data));
        }
        if (source == SourceMsm && code == MsmSignalQualityChange && size >= sizeof(int))
        {
            return notification with { SignalQuality = Marshal.ReadInt32(data) };
        }
        if (source == SourceMsm && code is >= MsmAssociating and <= MsmRoamingEnd or >= MsmDisassociating and <= MsmAdapterRemoval
            && size >= MsmDataSize)
        {
            var bssid = new byte[6];
            Marshal.Copy(data + MsmBssidOffset, bssid, 0, bssid.Length);
            return WithReason(notification with { Ssid = Ssid(data), WindowsProfileName = ProfileName(data), Bssid = BitConverter.ToString(bssid) },
                Marshal.ReadInt32(data + MsmReasonOffset));
        }
        return notification;
    }

    // Нулевой код — «причины нет»; он не записывается, чтобы не загромождать ленту
    private static WlanNotification WithReason(WlanNotification notification, int reason) =>
        reason == 0 ? notification : notification with { ReasonCode = reason, Reason = ReasonText(reason) };

    private static string? ReasonText(int reason)
    {
        var text = new StringBuilder(512);
        return WlanReasonCodeToString((uint)reason, text.Capacity, text, IntPtr.Zero) == 0 ? text.ToString() : null;
    }

    // Общее начало WLAN_CONNECTION_NOTIFICATION_DATA и WLAN_MSM_NOTIFICATION_DATA:
    // WLAN_CONNECTION_MODE, WCHAR strProfileName[256], DOT11_SSID { ULONG uSSIDLength; UCHAR ucSSID[32]; }
    private const int ProfileNameOffset = 4;
    private const int ProfileNameLength = 256;
    private const int SsidLengthOffset = 516;
    private const int SsidOffset = 520;
    private const int SsidMaxLength = 32;

    // WLAN_CONNECTION_NOTIFICATION_DATA: …, DOT11_BSS_TYPE, BOOL bSecurityEnabled, WLAN_REASON_CODE, DWORD dwFlags, WCHAR strProfileXml[1]
    private const int ConnectionReasonOffset = 560;
    private const int ConnectionDataSize = 564;

    // WLAN_MSM_NOTIFICATION_DATA: …, DOT11_BSS_TYPE, DOT11_MAC_ADDRESS, BOOL bSecurityEnabled, BOOL bFirstPeer, BOOL bLastPeer, WLAN_REASON_CODE
    private const int MsmBssidOffset = 556;
    private const int MsmReasonOffset = 576;
    private const int MsmDataSize = 580;

    private static string ProfileName(IntPtr data) => Marshal.PtrToStringUni(data + ProfileNameOffset, ProfileNameLength).TrimEnd('\0').Split('\0')[0];

    private static string Ssid(IntPtr data)
    {
        int length = Math.Clamp(Marshal.ReadInt32(data + SsidLengthOffset), 0, SsidMaxLength);
        var bytes = new byte[length];
        Marshal.Copy(data + SsidOffset, bytes, 0, length);
        // Так же SSID читает ManagedNativeWifi
        return Encoding.UTF8.GetString(bytes);
    }

    private const int AcmScanFail = 8;
    private const int AcmConnectionStart = 9;
    private const int AcmConnectionComplete = 10;
    private const int AcmConnectionAttemptFail = 11;
    private const int AcmDisconnecting = 20;
    private const int AcmDisconnected = 21;
    private const int MsmAssociating = 1;
    private const int MsmRoamingEnd = 6;
    private const int MsmSignalQualityChange = 8;
    private const int MsmDisassociating = 9;
    private const int MsmAdapterRemoval = 13;

    // wlan_notification_acm_* и wlan_notification_msm_* из wlanapi.h по порядку значений
    private static readonly string[] AcmCodes =
    [
        "start", "autoconf_enabled", "autoconf_disabled", "background_scan_enabled", "background_scan_disabled",
        "bss_type_change", "power_setting_change", "scan_complete", "scan_fail", "connection_start",
        "connection_complete", "connection_attempt_fail", "filter_list_change", "interface_arrival", "interface_removal",
        "profile_change", "profile_name_change", "profiles_exhausted", "network_not_available", "network_available",
        "disconnecting", "disconnected", "adhoc_network_state_change", "profile_unblocked", "screen_power_change",
        "profile_blocked", "scan_list_refresh", "operation_mode_change",
    ];

    private static readonly string[] MsmCodes =
    [
        "start", "associating", "associated", "authenticating", "connected", "roaming_start", "roaming_end",
        "radio_state_change", "signal_quality_change", "disassociating", "disconnected", "peer_join", "peer_leave",
        "adapter_removal", "adapter_operation_mode_change", "link_degraded", "link_improved",
    ];

    internal static string CodeName(int source, int code)
    {
        string[]? names = source switch { SourceAcm => AcmCodes, SourceMsm => MsmCodes, _ => null };
        return names != null && code >= 0 && code < names.Length ? names[code] : "?";
    }

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

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern int WlanReasonCodeToString(uint reasonCode, int bufferSize, StringBuilder buffer, IntPtr reserved);
}
