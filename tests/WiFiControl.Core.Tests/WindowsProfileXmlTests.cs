using System.Xml.Linq;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

/// <summary>XML профиля сети Windows по схеме WLAN_profile (learn.microsoft.com, «WLAN profile schema»).</summary>
public class WindowsProfileXmlTests
{
    private static readonly XNamespace V1 = "http://www.microsoft.com/networking/WLAN/profile/v1";
    private static readonly XNamespace V4 = "http://www.microsoft.com/networking/WLAN/profile/v4";
    private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static XElement Parse(NewWindowsProfile profile) => XDocument.Parse(profile.Xml).Root!;

    private static string? Value(XElement root, params string[] path)
    {
        XElement? element = root;
        foreach (string name in path) element = element?.Element(V1 + name);
        return element?.Value;
    }

    private static XElement? Security(XElement root) => root.Element(V1 + "MSM")?.Element(V1 + "security");

    [Fact]
    public void Name_and_ssid_come_from_the_network_key()
    {
        XElement root = Parse(WindowsProfileXml.Build("Кафе & Co", NewNetworkSecurity.Open, "", autoConnect: true));

        Assert.Equal("Кафе & Co", Value(root, "name"));
        Assert.Equal("Кафе & Co", Value(root, "SSIDConfig", "SSID", "name"));
        Assert.Equal("D09AD0B0D184D0B5202620436F", Value(root, "SSIDConfig", "SSID", "hex"));
        Assert.Equal("ESS", Value(root, "connectionType"));
    }

    [Fact]
    public void Network_whose_name_is_not_utf8_gets_its_bytes_in_hex()
    {
        NewWindowsProfile profile = WindowsProfileXml.Build("hex:CFF0E8E2E5F2", NewNetworkSecurity.Open, "", autoConnect: true);

        Assert.Equal("hex:CFF0E8E2E5F2", profile.Name);
        Assert.Equal("CFF0E8E2E5F2", Value(Parse(profile), "SSIDConfig", "SSID", "hex"));
    }

    [Theory]
    [InlineData(true, "auto")]
    [InlineData(false, "manual")]
    public void Auto_connect_is_the_connection_mode(bool autoConnect, string mode)
    {
        XElement root = Parse(WindowsProfileXml.Build("Cafe", NewNetworkSecurity.Wpa2Personal, "12345678", autoConnect));

        Assert.Equal(mode, Value(root, "connectionMode"));
    }

    [Fact]
    public void Open_network_has_no_encryption_and_no_key()
    {
        XElement security = Security(Parse(WindowsProfileXml.Build("Cafe", NewNetworkSecurity.Open, "", autoConnect: true)))!;

        Assert.Equal("open", Value(security, "authEncryption", "authentication"));
        Assert.Equal("none", Value(security, "authEncryption", "encryption"));
        Assert.Equal("false", Value(security, "authEncryption", "useOneX"));
        Assert.Null(security.Element(V1 + "sharedKey"));
    }

    [Theory]
    [InlineData(NewNetworkSecurity.Wpa2Personal, "WPA2PSK", null)]
    [InlineData(NewNetworkSecurity.Wpa3Personal, "WPA3SAE", null)]
    [InlineData(NewNetworkSecurity.Wpa2Wpa3Personal, "WPA3SAE", "true")]
    public void Personal_network_has_aes_and_its_authentication(NewNetworkSecurity kind, string authentication, string? transitionMode)
    {
        XElement security = Security(Parse(WindowsProfileXml.Build("Cafe", kind, "12345678", autoConnect: true)))!;

        XElement authEncryption = security.Element(V1 + "authEncryption")!;
        Assert.Equal(authentication, Value(authEncryption, "authentication"));
        Assert.Equal("AES", Value(authEncryption, "encryption"));
        Assert.Equal("false", Value(authEncryption, "useOneX"));
        Assert.Equal(transitionMode, authEncryption.Element(V4 + "transitionMode")?.Value);
    }

    [Theory]
    [InlineData("pass 12345", "passPhrase")]
    [InlineData(Key, "networkKey")]
    public void Password_goes_unprotected_as_a_passphrase_or_a_key(string password, string keyType)
    {
        XElement security = Security(Parse(WindowsProfileXml.Build("Cafe", NewNetworkSecurity.Wpa2Personal, password, autoConnect: true)))!;

        Assert.Equal(keyType, Value(security, "sharedKey", "keyType"));
        Assert.Equal("false", Value(security, "sharedKey", "protected"));
        Assert.Equal(password, Value(security, "sharedKey", "keyMaterial"));
    }

    [Theory]
    [InlineData(NewNetworkSecurity.Wpa2Personal, "1234567")]
    [InlineData(NewNetworkSecurity.Open, "12345678")]
    // У SAE нет ключа, только пароль (схема WLAN_profile, sharedKey)
    [InlineData(NewNetworkSecurity.Wpa3Personal, Key)]
    [InlineData(NewNetworkSecurity.Wpa2Wpa3Personal, Key)]
    public void Password_that_does_not_fit_the_security_is_refused(NewNetworkSecurity kind, string password)
    {
        Assert.Throws<ArgumentException>(() => WindowsProfileXml.Build("Cafe", kind, password, autoConnect: true));
    }

    [Fact]
    public void New_password_replaces_the_protected_key_and_keeps_the_rest()
    {
        // Так Windows отдаёт профиль сети Windows: пароль зашифрован
        const string existing = """
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>Office</name>
              <SSIDConfig><SSID><hex>4F6666696365</hex><name>Office</name></SSID></SSIDConfig>
              <connectionType>ESS</connectionType>
              <connectionMode>manual</connectionMode>
              <MSM><security>
                <authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>
                <sharedKey><keyType>passPhrase</keyType><protected>true</protected><keyMaterial>01000000D08C9DDF</keyMaterial></sharedKey>
              </security></MSM>
            </WLANProfile>
            """;

        XElement root = XDocument.Parse(WindowsProfileXml.WithPassword(existing, Key)).Root!;

        Assert.Equal("manual", Value(root, "connectionMode"));
        Assert.Equal("WPA2PSK", Value(Security(root)!, "authEncryption", "authentication"));
        Assert.Equal("networkKey", Value(Security(root)!, "sharedKey", "keyType"));
        Assert.Equal("false", Value(Security(root)!, "sharedKey", "protected"));
        Assert.Equal(Key, Value(Security(root)!, "sharedKey", "keyMaterial"));
    }

    [Fact]
    public void Sae_profile_cannot_get_a_key_as_its_new_password()
    {
        string sae = WindowsProfileXml.Build("Cafe", NewNetworkSecurity.Wpa2Wpa3Personal, "12345678", autoConnect: true).Xml;

        var error = Assert.Throws<InvalidOperationException>(() => WindowsProfileXml.WithPassword(sae, Key));
        Assert.Equal("Сеть «Cafe» с WPA3 принимает только пароль от 8 до 63 символов, а не ключ из 64 шестнадцатеричных цифр.", error.Message);
    }

    [Theory]
    [InlineData(NewNetworkSecurity.Wpa2Personal, true)]
    [InlineData(NewNetworkSecurity.Wpa3Personal, false)]
    [InlineData(NewNetworkSecurity.Wpa2Wpa3Personal, false)]
    public void Only_wpa2_takes_a_key(NewNetworkSecurity kind, bool accepted)
    {
        Assert.Equal(accepted, WindowsProfileXml.AcceptsKey(kind));
    }

    [Fact]
    public void Profile_without_a_shared_key_cannot_get_a_new_password()
    {
        string open = WindowsProfileXml.Build("Cafe", NewNetworkSecurity.Open, "", autoConnect: true).Xml;

        var error = Assert.Throws<InvalidOperationException>(() => WindowsProfileXml.WithPassword(open, "12345678"));
        Assert.Equal("В профиле сети Windows «Cafe» нет пароля, который можно заменить.", error.Message);
    }

    [Theory]
    [InlineData(WifiSecurity.Open, NewNetworkSecurity.Open)]
    [InlineData(WifiSecurity.Wpa2Personal, NewNetworkSecurity.Wpa2Personal)]
    // WPA3 и смешанный режим Windows в списке сетей не различает; смешанный подходит обоим
    [InlineData(WifiSecurity.Wpa3Personal, NewNetworkSecurity.Wpa2Wpa3Personal)]
    [InlineData(WifiSecurity.Wep, null)]
    [InlineData(WifiSecurity.WpaPersonal, null)]
    [InlineData(WifiSecurity.Enterprise, null)]
    [InlineData(WifiSecurity.Other, null)]
    public void Profile_is_created_only_for_open_and_wpa2_wpa3_personal_networks(WifiSecurity seen, NewNetworkSecurity? created)
    {
        Assert.Equal(created, WindowsProfileXml.SecurityFor(seen));
    }
}
