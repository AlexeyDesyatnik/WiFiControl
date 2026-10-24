using System.Runtime.InteropServices;
using System.Text;
using WiFiControl.Recon.Model;
using WiFiControl.Recon.Windows;

namespace WiFiControl.Recon.Tests;

/// <summary>Разбор данных уведомлений WLAN по разметке структур из wlanapi.h.</summary>
public class WlanNotificationsTests
{
    private static readonly Guid Adapter = FakeSystem.WifiId;

    private static WlanNotification Read(int source, int code, byte[] data)
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

    // Общее начало WLAN_CONNECTION_NOTIFICATION_DATA и WLAN_MSM_NOTIFICATION_DATA
    private static byte[] Head(int size, string profile, string ssid)
    {
        var data = new byte[size];
        BitConverter.GetBytes(1).CopyTo(data, 0);
        Encoding.Unicode.GetBytes(profile).CopyTo(data, 4);
        byte[] ssidBytes = Encoding.UTF8.GetBytes(ssid);
        BitConverter.GetBytes(ssidBytes.Length).CopyTo(data, 516);
        ssidBytes.CopyTo(data, 520);
        return data;
    }

    [Fact]
    public void Acm_connection_data_gives_ssid_profile_and_reason()
    {
        byte[] data = Head(568, "Дача", "Dacha-5G");
        BitConverter.GetBytes(0x00038002).CopyTo(data, 560);

        WlanNotification notification = Read(WlanNotifications.SourceAcm, 11, data);

        Assert.Equal("ACM", notification.Source);
        Assert.Equal("connection_attempt_fail", notification.CodeName);
        Assert.Equal("Dacha-5G", notification.Ssid);
        Assert.Equal("Дача", notification.WindowsProfileName);
        Assert.Equal(0x00038002, notification.ReasonCode);
        Assert.Null(notification.Bssid);
    }

    [Fact]
    public void Msm_data_gives_ssid_profile_and_bssid()
    {
        byte[] data = Head(580, "Office", "Office");
        new byte[] { 0xCC, 0x2D, 0x21, 0x3B, 0x83, 0x96 }.CopyTo(data, 556);

        WlanNotification notification = Read(WlanNotifications.SourceMsm, 4, data);

        Assert.Equal("MSM", notification.Source);
        Assert.Equal("connected", notification.CodeName);
        Assert.Equal("Office", notification.Ssid);
        Assert.Equal("Office", notification.WindowsProfileName);
        Assert.Equal("CC-2D-21-3B-83-96", notification.Bssid);
        // Нулевой код причины — «причины нет», он не записывается
        Assert.Null(notification.ReasonCode);
    }

    [Fact]
    public void Signal_quality_is_read_from_its_notification()
    {
        WlanNotification notification = Read(WlanNotifications.SourceMsm, 8, BitConverter.GetBytes(64));

        Assert.Equal("signal_quality_change", notification.CodeName);
        Assert.Equal(64, notification.SignalQuality);
        Assert.Null(notification.Ssid);
    }

    [Fact]
    public void Notification_without_data_keeps_only_source_and_code()
    {
        WlanNotification notification = Read(WlanNotifications.SourceAcm, 7, []);

        Assert.Equal(new WlanNotification(Adapter, "ACM", 7, "scan_complete"), notification);
    }

    [Fact]
    public void Data_shorter_than_its_structure_is_not_read()
    {
        WlanNotification notification = Read(WlanNotifications.SourceAcm, 10, new byte[100]);

        Assert.Equal("connection_complete", notification.CodeName);
        Assert.Null(notification.Ssid);
    }

    [Theory]
    [InlineData(WlanNotifications.SourceAcm, 21, "disconnected")]
    [InlineData(WlanNotifications.SourceAcm, 27, "operation_mode_change")]
    [InlineData(WlanNotifications.SourceMsm, 10, "disconnected")]
    [InlineData(WlanNotifications.SourceMsm, 16, "link_improved")]
    [InlineData(WlanNotifications.SourceMsm, 99, "?")]
    public void Codes_are_named_as_in_wlanapi_h(int source, int code, string name)
    {
        Assert.Equal(name, WlanNotifications.CodeName(source, code));
    }
}
