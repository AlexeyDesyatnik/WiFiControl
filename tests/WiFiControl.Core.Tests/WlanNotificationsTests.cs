using System.Runtime.InteropServices;
using System.Text;
using WiFiControl.Core.Model;
using WiFiControl.Core.Windows;

namespace WiFiControl.Core.Tests;

/// <summary>Разбор данных уведомлений WLAN по разметке структур из wlanapi.h.</summary>
public class WlanNotificationsTests
{
    private const int Acm = 0x08;
    private const int Msm = 0x10;
    private static readonly Guid Adapter = FakeWifi.WifiId;

    private static WifiNotification? Read(int source, int code, byte[] data)
    {
        IntPtr buffer = Marshal.AllocHGlobal(Math.Max(data.Length, 1));
        try
        {
            Marshal.Copy(data, 0, buffer, data.Length);
            return WlanNotifications.Read(source, code, Adapter, buffer, data.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // WLAN_CONNECTION_NOTIFICATION_DATA: режим, имя профиля сети Windows, SSID, …, код причины
    private static byte[] Connection(byte[] ssid, int reason = 0)
    {
        var data = new byte[568];
        BitConverter.GetBytes(1).CopyTo(data, 0);
        Encoding.Unicode.GetBytes("Профиль").CopyTo(data, 4);
        BitConverter.GetBytes(ssid.Length).CopyTo(data, 516);
        ssid.CopyTo(data, 520);
        BitConverter.GetBytes(reason).CopyTo(data, 560);
        return data;
    }

    [Fact]
    public void Connection_complete_without_reason_is_a_connection_to_the_network()
    {
        WifiNotification? notification = Read(Acm, 10, Connection(Encoding.UTF8.GetBytes("Office")));

        Assert.Equal(new WifiConnected(Adapter, "Office"), notification);
    }

    [Fact]
    public void Network_in_notification_is_a_key_so_non_utf8_name_is_hex()
    {
        WifiNotification? notification = Read(Acm, 10, Connection([0xCF, 0xF0, 0xE8]));

        Assert.Equal(new WifiConnected(Adapter, "hex:CFF0E8"), notification);
    }

    [Fact]
    public void Attempt_fail_and_complete_with_reason_are_failures_with_the_reason_code()
    {
        // Неверный пароль, по ручной проверке подключения (docs/connect-check.md)
        Assert.Equal(new WifiConnectionAttemptFailed(Adapter, "Office", 0x48014),
            Read(Acm, 11, Connection(Encoding.UTF8.GetBytes("Office"), 0x48014)));
        Assert.Equal(new WifiConnectionFailed(Adapter, "Office", 0x2800B),
            Read(Acm, 10, Connection(Encoding.UTF8.GetBytes("Office"), 0x2800B)));
    }

    [Fact]
    public void Acm_disconnected_is_a_disconnection()
    {
        Assert.Equal(new WifiDisconnected(Adapter, "Office"), Read(Acm, 21, Connection(Encoding.UTF8.GetBytes("Office"))));
    }

    [Fact]
    public void Scan_complete_and_scan_fail_both_end_the_scan()
    {
        Assert.Equal(new WifiScanCompleted(Adapter), Read(Acm, 7, []));
        Assert.Equal(new WifiScanCompleted(Adapter), Read(Acm, 8, BitConverter.GetBytes(0x10001)));
    }

    [Fact]
    public void Other_notifications_and_truncated_data_are_skipped()
    {
        // MSM connected, ACM connection_start, ACM connection_complete без данных
        Assert.Null(Read(Msm, 4, new byte[580]));
        Assert.Null(Read(Acm, 9, Connection(Encoding.UTF8.GetBytes("Office"))));
        Assert.Null(Read(Acm, 10, new byte[16]));
    }
}
