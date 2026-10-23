using System.Management;
using WiFiControl.Recon.Diagnostics;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Tests;

public class ReconLogIpv4Tests
{
    private readonly FakeSystem _system = new();
    private readonly FakeClock _clock = new();

    private Reading Read() => new ReconLog(_system, _clock, "1.2.3-test").Read("при запуске");

    private static List<string> SectionLines(Reading reading, int count)
    {
        int section = reading.Lines.ToList().IndexOf("  [Настройки IPv4]");
        Assert.True(section >= 0, reading.Text);
        return reading.Lines.Skip(section + 1).Take(count).ToList();
    }

    [Fact]
    public void Wifi_adapter_on_dhcp_shows_automatic_settings_and_active_addresses()
    {
        Reading reading = Read();

        Assert.Equal(
            [
                "    [Intel(R) Wi-Fi 6 AX200 160MHz]",
                "      Адаптер в «Сетевых подключениях»: Беспроводная сеть, Connected",
                "      [Настроено]",
                "        Получение адреса: автоматически",
                "        Получение DNS: автоматически",
                "      [Действует]",
                "        Адреса: 192.168.1.37/24",
                "        Шлюзы: 192.168.1.1",
                "        DNS: 192.168.1.1",
                "        Конфликт адресов: нет",
            ],
            SectionLines(reading, 10));
        Assert.False(reading.HasErrors, reading.Text);
    }

    private void SetWifiAdapter(Func<Adapter, Adapter> change)
    {
        int index = _system.Adapters.FindIndex(a => a.Id == FakeSystem.WifiId);
        _system.Adapters[index] = change(_system.Adapters[index]);
    }

    [Fact]
    public void Manual_settings_show_address_mask_gateway_and_dns()
    {
        SetWifiAdapter(a => a with { Configured = new(false, "10.68.12.37", "255.255.255.0", "10.68.12.1", false, ["10.0.68.10", "10.0.68.11"]) });

        Reading reading = Read();

        Assert.Equal(
            [
                "      [Настроено]",
                "        Получение адреса: вручную",
                "        Адрес: 10.68.12.37",
                "        Маска: 255.255.255.0",
                "        Шлюз: 10.68.12.1",
                "        Получение DNS: вручную",
                "        DNS: 10.0.68.10, 10.0.68.11",
            ],
            SectionLines(reading, 9).Skip(2));
    }

    [Fact]
    public void Manual_dns_with_no_servers_is_shown_as_empty()
    {
        SetWifiAdapter(a => a with { Configured = new(false, "10.68.12.37", "255.255.255.0", "", false, []) });

        Reading reading = Read();

        Assert.Contains("        Шлюз: (пусто)", reading.Lines);
        Assert.Contains("        DNS: (пусто)", reading.Lines);
    }

    [Fact]
    public void Active_settings_are_not_read_without_a_connection()
    {
        SetWifiAdapter(a => a with { State = AdapterState.NoLink, Active = null });

        Reading reading = Read();

        Assert.Equal(
            [
                "      Адаптер в «Сетевых подключениях»: Беспроводная сеть, NoLink",
                "      [Настроено]",
                "        Получение адреса: автоматически",
                "        Получение DNS: автоматически",
                "      [Действует]",
                "        Не прочитано: нет соединения — сетевой стек отдал бы адрес 169.254.x.x и прежние DNS",
            ],
            SectionLines(reading, 7).Skip(1));
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Address_conflict_is_shown()
    {
        SetWifiAdapter(a => a with { Active = new([], [], [], true) });

        Reading reading = Read();

        Assert.Contains("        Адреса: (пусто)", reading.Lines);
        Assert.Contains("        Конфликт адресов: да", reading.Lines);
    }

    [Fact]
    public void Wifi_adapter_missing_from_win32_network_adapter_is_marked()
    {
        _system.Adapters.RemoveAll(a => a.Id == FakeSystem.WifiId);

        Reading reading = Read();

        Assert.Equal(
            ["    [Intel(R) Wi-Fi 6 AX200 160MHz]", "      Не прочитано: в «Сетевых подключениях» (Win32_NetworkAdapter) нет адаптера с таким GUID"],
            SectionLines(reading, 2));
    }

    [Fact]
    public void Failure_to_read_adapters_is_recorded_in_the_ipv4_section()
    {
        _system.AdaptersFailure = new ManagementException("Недопустимый запрос");

        Reading reading = Read();

        Assert.StartsWith("    ОШИБКА: System.Management.ManagementException", SectionLines(reading, 1)[0]);
        Assert.Contains("      SSID: Office", reading.Lines);
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Ipv4_settings_are_not_read_without_the_wifi_adapter_list()
    {
        _system.WifiAdaptersFailure = new System.ComponentModel.Win32Exception(1062);

        Reading reading = Read();

        Assert.Equal(["    Не прочитано: нет списка Wi-Fi-адаптеров"], SectionLines(reading, 1));
    }

    [Fact]
    public void Computer_without_wifi_adapters_has_no_ipv4_settings_to_show()
    {
        _system.WifiAdapters.Clear();

        Reading reading = Read();

        Assert.Equal(["    Wi-Fi-адаптеров нет"], SectionLines(reading, 1));
        Assert.False(reading.HasErrors, reading.Text);
    }
}
