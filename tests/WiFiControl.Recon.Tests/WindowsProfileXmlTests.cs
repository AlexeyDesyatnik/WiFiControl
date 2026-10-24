using WiFiControl.Recon.Model;
using WiFiControl.Recon.Windows;

namespace WiFiControl.Recon.Tests;

public class WindowsProfileXmlTests
{
    // Так WlanGetProfile отдаёт профиль на Windows 11: ключ зашифрован, MacRandomization — в пространстве имён v3
    private const string OfficeXml = """
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
          <name>Office</name>
          <SSIDConfig>
            <SSID>
              <hex>4F6666696365</hex>
              <name>Office</name>
            </SSID>
          </SSIDConfig>
          <connectionType>ESS</connectionType>
          <connectionMode>auto</connectionMode>
          <MSM>
            <security>
              <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
              </authEncryption>
              <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>true</protected>
                <keyMaterial>01000000D08C9DDF0115D1118C7A00C04FC297EB</keyMaterial>
              </sharedKey>
            </security>
          </MSM>
          <MacRandomization xmlns="http://www.microsoft.com/networking/WLAN/profile/v3">
            <enableRandomization>false</enableRandomization>
            <randomizationSeed>1921880196</randomizationSeed>
          </MacRandomization>
        </WLANProfile>
        """;

    [Fact]
    public void Reads_name_ssid_connection_mode_and_mac_randomization()
    {
        WindowsProfile profile = WindowsProfileXml.Read("Office", "AllUser", 0, OfficeXml);

        Assert.Equal("Office", profile.Name);
        Assert.Equal("AllUser", profile.Type);
        Assert.Equal(0, profile.Position);
        Assert.Equal("Office", profile.Ssid);
        Assert.Equal("auto", profile.ConnectionMode);
        Assert.Equal([new XmlValue("enableRandomization", "false"), new XmlValue("randomizationSeed", "1921880196")], profile.MacRandomization);
    }

    [Fact]
    public void Network_key_does_not_get_into_the_profile()
    {
        WindowsProfile profile = WindowsProfileXml.Read("Office", "AllUser", 0, OfficeXml);

        string[] values = [profile.Name, profile.Type, profile.Ssid!, profile.ConnectionMode!, .. profile.MacRandomization!.Select(value => value.Value)];
        Assert.DoesNotContain(values, value => value.Contains("01000000D08C9DDF"));
    }

    [Fact]
    public void Missing_elements_are_null()
    {
        const string xml = """
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>Гости</name>
              <SSIDConfig><nonBroadcast>true</nonBroadcast></SSIDConfig>
              <connectionType>ESS</connectionType>
            </WLANProfile>
            """;

        WindowsProfile profile = WindowsProfileXml.Read("Гости", "PerUser", 2, xml);

        Assert.Null(profile.Ssid);
        Assert.Null(profile.ConnectionMode);
        Assert.Null(profile.MacRandomization);
    }
}
