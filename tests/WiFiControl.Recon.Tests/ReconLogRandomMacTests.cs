using System.ComponentModel;
using System.Net.NetworkInformation;
using WiFiControl.Recon.Diagnostics;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Tests;

public class ReconLogRandomMacTests
{
    private readonly FakeSystem _system = new();
    private readonly FakeClock _clock = new();

    private Reading Read() => new ReconLog(_system, _clock, "1.2.3-test").Read("при запуске");

    private static List<string> Section(Reading reading)
    {
        List<string> lines = reading.Lines.ToList();
        int start = lines.IndexOf("  [Случайный MAC-адрес]");
        Assert.True(start >= 0, reading.Text);
        return lines.Skip(start + 1).TakeWhile(line => line.StartsWith("    ", StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public void Sources_that_agree_are_shown_side_by_side()
    {
        Reading reading = Read();

        Assert.Equal(
            [
                "    [Intel(R) Wi-Fi 6 AX200 160MHz]",
                "      [Адреса адаптера]",
                "        Текущий: A4-C3-F0-11-22-33",
                "        Постоянный: A4-C3-F0-11-22-33",
                "        Текущий отличается от постоянного: нет",
                "        Текущий локально администрируемый: нет",
                "      [Реестр]",
                @"        Раздел службы WLAN: HKLM\SOFTWARE\Microsoft\WlanSvc\Interfaces\{b0fbe93a-b7d1-490e-9b93-c44d06b82760}",
                "        RandomMacState: значения нет",
                @"        Раздел драйвера: HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\0018",
                "        NetworkAddress (адрес, заданный вручную): значения нет",
                "      [XML профиля текущей сети Windows]",
                "        Профиль сети Windows: Office",
                "        MacRandomization: enableRandomization = false, randomizationSeed = 1921880196",
            ],
            Section(reading));
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Sources_that_disagree_are_shown_as_they_are()
    {
        // Адрес случайный, а в XML профиля случайный MAC-адрес выключен
        _system.MacAddresses = new("DA-91-5E-07-4C-B2", "A4-C3-F0-11-22-33");
        _system.MacRegistry = _system.MacRegistry with { RandomMacState = new byte[] { 1, 0, 0, 0 } };

        Reading reading = Read();

        List<string> section = Section(reading);
        Assert.Contains("        Текущий: DA-91-5E-07-4C-B2", section);
        Assert.Contains("        Текущий отличается от постоянного: да", section);
        Assert.Contains("        Текущий локально администрируемый: да — так выглядит случайный адрес", section);
        Assert.Contains("        RandomMacState: 01 00 00 00", section);
        Assert.Contains("        MacRandomization: enableRandomization = false, randomizationSeed = 1921880196", section);
    }

    [Fact]
    public void Address_set_by_hand_in_the_driver_is_shown()
    {
        _system.MacAddresses = new("02-11-22-33-44-55", "A4-C3-F0-11-22-33");
        _system.MacRegistry = _system.MacRegistry with { NetworkAddress = "021122334455" };

        Reading reading = Read();

        Assert.Contains("        NetworkAddress (адрес, заданный вручную): 021122334455", Section(reading));
    }

    [Fact]
    public void Missing_registry_keys_are_marked()
    {
        _system.MacRegistry = new(null, null, null, null);

        Reading reading = Read();

        List<string> section = Section(reading);
        int registry = section.IndexOf("      [Реестр]");
        Assert.Equal(["        Раздел службы WLAN: раздела нет", "        Раздел драйвера: не найден"], section.Skip(registry + 1).Take(2));
    }

    [Fact]
    public void Profile_without_mac_randomization_element_is_marked()
    {
        _system.WindowsProfiles = [new("Office", "AllUser", 0, "Office", "auto", null)];

        Reading reading = Read();

        Assert.Contains("        MacRandomization: элемента MacRandomization нет", Section(reading));
    }

    [Fact]
    public void Current_profile_missing_from_the_profile_list_is_marked()
    {
        _system.WindowsProfiles.RemoveAll(profile => profile.Name == "Office");

        Reading reading = Read();

        Assert.Contains("        MacRandomization: нет — такого профиля нет в списке профилей сетей Windows", Section(reading));
    }

    [Fact]
    public void Profile_xml_is_not_read_without_a_connection()
    {
        _system.Connections.Clear();

        Reading reading = Read();

        List<string> section = Section(reading);
        int xml = section.IndexOf("      [XML профиля текущей сети Windows]");
        Assert.Equal("        Не прочитано: адаптер не подключён — текущей сети нет", section[xml + 1]);
        Assert.Contains("        Текущий: A4-C3-F0-11-22-33", section);
    }

    [Fact]
    public void Each_failing_source_is_recorded_with_its_code_and_the_others_stay()
    {
        _system.MacAddressesFailure = new Win32Exception(1168, "GetIfEntry2: Элемент не найден");
        _system.MacRegistryFailure = new UnauthorizedAccessException("Доступ к разделу реестра запрещён.");
        _system.WindowsProfilesFailure = new NetworkInformationException(5);

        Reading reading = Read();

        List<string> section = Section(reading);
        Assert.Equal("        ОШИБКА: System.ComponentModel.Win32Exception (код 1168): GetIfEntry2: Элемент не найден",
            section[section.IndexOf("      [Адреса адаптера]") + 1]);
        Assert.StartsWith("        ОШИБКА: System.UnauthorizedAccessException", section[section.IndexOf("      [Реестр]") + 1]);
        int xml = section.IndexOf("      [XML профиля текущей сети Windows]");
        Assert.Equal("        Профиль сети Windows: Office", section[xml + 1]);
        Assert.StartsWith("        ОШИБКА: System.Net.NetworkInformation.NetworkInformationException (код 5)", section[xml + 2]);
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Section_is_not_read_without_the_list_of_wifi_adapters()
    {
        _system.WifiAdaptersFailure = new Win32Exception(1062, "WlanEnumInterfaces: служба не запущена");

        Reading reading = Read();

        Assert.Equal(["    Не прочитано: нет списка Wi-Fi-адаптеров"], Section(reading));
    }

    [Theory]
    [InlineData("A4-C3-F0-11-22-33", false)]
    [InlineData("DA-91-5E-07-4C-B2", true)]
    [InlineData("02-00-00-00-00-01", true)]
    [InlineData("3C-21-9C-4E-8F-40", false)]
    public void Locally_administered_bit_is_read_from_the_first_byte(string mac, bool expected)
    {
        Assert.Equal(expected, MacAddresses.IsLocallyAdministered(mac));
    }
}
