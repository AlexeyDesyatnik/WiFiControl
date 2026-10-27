using System.Text;
using WiFiControl.Core.Model;
using WiFiControl.Core.Windows;

namespace WiFiControl.Core.Tests;

// Перенесено из NetSwitch
public class InternetSettingsBlobTests
{
    private static byte[] Blob(int flags, params string[] strings)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(0x46));
        bytes.AddRange(BitConverter.GetBytes(5));
        bytes.AddRange(BitConverter.GetBytes(flags));
        foreach (string text in strings)
        {
            bytes.AddRange(BitConverter.GetBytes(text.Length));
            bytes.AddRange(Encoding.ASCII.GetBytes(text));
        }
        return [.. bytes];
    }

    private static SystemProxyState State(bool enabled, string server, string bypass, bool autoDetect = false, string script = "")
    {
        return new SystemProxyState { Settings = ProxySettings.FromRaw(enabled, server, bypass), AutoDetect = autoDetect, AutoConfigUrl = script };
    }

    [Fact]
    public void Reads_manual_proxy_with_bypass_list()
    {
        InternetSettingsBlob blob = InternetSettingsBlob.Parse(Blob(0x3, "10.0.68.52:3128", "<local>", ""));

        Assert.Equal(0x46, blob.Version);
        Assert.Equal(5, blob.Counter);
        Assert.True(blob.ProxyEnabled);
        Assert.False(blob.AutoDetect);
        Assert.False(blob.AutoConfigEnabled);
        Assert.Equal("10.0.68.52:3128", blob.Proxy);
        Assert.Equal("<local>", blob.Bypass);
        Assert.Equal("", blob.AutoConfigUrl);
    }

    [Fact]
    public void Reads_auto_detect_and_script()
    {
        InternetSettingsBlob blob = InternetSettingsBlob.Parse(Blob(0x1 | 0x4 | 0x8, "", "", "http://wpad.corp.local/proxy.pac"));

        Assert.False(blob.ProxyEnabled);
        Assert.True(blob.AutoDetect);
        Assert.True(blob.AutoConfigEnabled);
        Assert.Equal("http://wpad.corp.local/proxy.pac", blob.AutoConfigUrl);
    }

    [Fact]
    public void Trailing_bytes_after_the_strings_are_ignored()
    {
        var data = new List<byte>(Blob(0x9, "", "", ""));
        data.AddRange(new byte[32]);

        Assert.True(InternetSettingsBlob.Parse([.. data]).AutoDetect);
    }

    [Fact]
    public void Short_winhttp_form_without_script_is_accepted()
    {
        InternetSettingsBlob blob = InternetSettingsBlob.Parse(Blob(0x3, "proxy:3128", "*.local"));

        Assert.Equal("proxy:3128", blob.Proxy);
        Assert.Equal("*.local", blob.Bypass);
        Assert.Equal("", blob.AutoConfigUrl);
    }

    [Fact]
    public void Header_only_value_means_direct_connection()
    {
        InternetSettingsBlob blob = InternetSettingsBlob.Parse(Blob(0x1));

        Assert.False(blob.ProxyEnabled);
        Assert.Equal("", blob.Proxy);
    }

    [Fact]
    public void Value_shorter_than_header_is_rejected()
    {
        Assert.Throws<FormatException>(() => InternetSettingsBlob.Parse(new byte[8]));
    }

    [Fact]
    public void String_running_past_the_end_is_rejected()
    {
        var data = new List<byte>(Blob(0x3));
        data.AddRange(BitConverter.GetBytes(100));
        data.AddRange(Encoding.ASCII.GetBytes("proxy"));

        Assert.Throws<FormatException>(() => InternetSettingsBlob.Parse([.. data]));
    }

    [Fact]
    public void Built_value_reads_back_as_the_same_proxy()
    {
        InternetSettingsBlob blob = InternetSettingsBlob.Parse(InternetSettingsBlob.Build(null, State(true, "10.0.68.52:3128", "*.corp.local;<local>")));

        Assert.Equal(0x46, blob.Version);
        Assert.Equal(0x3, blob.Flags);
        Assert.Equal("10.0.68.52:3128", blob.Proxy);
        Assert.Equal("*.corp.local;<local>", blob.Bypass);
        Assert.Equal("", blob.AutoConfigUrl);
        Assert.Equal(32, blob.Tail.Length);
    }

    [Fact]
    public void Built_value_carries_private_networks_as_exceptions()
    {
        var state = new SystemProxyState { Settings = new ProxySettings { Enabled = true, Server = "proxy:3128", PrivateNetworksDirect = true } };

        InternetSettingsBlob blob = InternetSettingsBlob.Parse(InternetSettingsBlob.Build(null, state));

        Assert.StartsWith("10.*;192.168.*;172.16.*;", blob.Bypass);
        Assert.EndsWith(";172.31.*", blob.Bypass);
    }

    [Fact]
    public void Disabled_proxy_keeps_server_but_clears_the_proxy_flag()
    {
        InternetSettingsBlob blob = InternetSettingsBlob.Parse(InternetSettingsBlob.Build(null, State(false, "10.0.68.52:3128", "<local>")));

        Assert.Equal(0x1, blob.Flags);
        Assert.False(blob.ProxyEnabled);
        Assert.Equal("10.0.68.52:3128", blob.Proxy);
    }

    [Fact]
    public void Auto_detect_and_script_are_written_when_asked_for()
    {
        InternetSettingsBlob blob = InternetSettingsBlob.Parse(InternetSettingsBlob.Build(null, State(false, "", "", true, "http://wpad/proxy.pac")));

        Assert.Equal(0x1 | 0x4 | 0x8, blob.Flags);
        Assert.Equal("http://wpad/proxy.pac", blob.AutoConfigUrl);
    }

    [Fact]
    public void Rewriting_bumps_the_counter_and_carries_over_what_windows_keeps_after_the_strings()
    {
        // Значение с Windows 7 из отчёта разведочной сборки NetSwitch: автоопределение включено, после строк — служебные данные
        var original = new List<byte>();
        original.AddRange([0x46, 0, 0, 0, 0x11, 0, 0, 0, 0x09, 0, 0, 0, 0x0F, 0, 0, 0]);
        original.AddRange(Encoding.ASCII.GetBytes("10.0.68.52:3128"));
        original.AddRange([0x07, 0, 0, 0]);
        original.AddRange(Encoding.ASCII.GetBytes("<local>"));
        original.AddRange([0, 0, 0, 0]);
        byte[] tail = [0x05, 0, 0, 0, 0xFD, 0xD4, 0xFC, 0x4C, 0x29, 0xF8, 0x17, 0x00, 0xC0, 0xA8, 0x01, 0x66];
        original.AddRange(tail);
        InternetSettingsBlob previous = InternetSettingsBlob.Parse([.. original]);

        InternetSettingsBlob rewritten = InternetSettingsBlob.Parse(InternetSettingsBlob.Build(previous, State(true, "10.0.68.52:3128", "<local>")));

        Assert.True(previous.AutoDetect);
        Assert.Equal(0x12, rewritten.Counter);
        Assert.Equal(0x3, rewritten.Flags);
        Assert.False(rewritten.AutoDetect);
        Assert.Equal(tail, rewritten.Tail);
    }
}
