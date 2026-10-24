using System.ComponentModel;
using WiFiControl.Recon.Diagnostics;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Tests;

public class ReconLogNetworksTests
{
    private readonly FakeSystem _system = new();
    private readonly FakeClock _clock = new();

    private Reading Read() => new ReconLog(_system, _clock, "1.2.3-test").Read("при запуске");

    private static List<string> Section(Reading reading, string title)
    {
        List<string> lines = reading.Lines.ToList();
        int start = lines.IndexOf("  [" + title + "]");
        Assert.True(start >= 0, reading.Text);
        return lines.Skip(start + 1).TakeWhile(line => line.StartsWith("    ", StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public void Visible_networks_are_read_after_the_scan_with_their_access_points()
    {
        _system.OnScan = () => _clock.Now += TimeSpan.FromMilliseconds(2350);

        Reading reading = Read();

        Assert.Equal(
            [
                "    [Intel(R) Wi-Fi 6 AX200 160MHz]",
                "      [Сканирование]",
                "        Ждём уведомления о конце не дольше: 10 с",
                "        Уведомление о конце: пришло через 2.35 с",
                "      Сетей: 2",
                "      [Office]",
                "        С профилем сети Windows «Office»: сигнал 80 %, защита RSNA_PSK, CCMP",
                "        Без профиля сети Windows: сигнал 88 %, защита RSNA_PSK, CCMP",
                "        [Точки доступа]",
                "          CC-2D-21-3B-83-96: 5180 МГц (5 ГГц, канал 36), -61 дБм",
                "          CC-2D-21-3B-83-92: 2412 МГц (2.4 ГГц, канал 1), -50 дБм",
                "      [Cafe]",
                "        Без профиля сети Windows: сигнал 72 %, без защиты: IEEE80211_Open, None",
                "        [Точки доступа]",
                "          5C-A6-E6-81-69-0B: 2417 МГц (2.4 ГГц, канал 2), -68 дБм",
            ],
            Section(reading, "Видимые сети"));
        Assert.Equal(1, _system.Scans);
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Scan_timeout_is_marked_with_the_wait_and_the_list_is_still_read()
    {
        _system.ScanCompletes = false;
        _system.OnScan = () => _clock.Now += TimeSpan.FromMilliseconds(10_004);

        Reading reading = Read();

        List<string> networks = Section(reading, "Видимые сети");
        Assert.Contains("        Уведомление о конце: не пришло за 10.004 с — список сетей прочитан без него", networks);
        Assert.Contains("      Сетей: 2", networks);
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Scan_failure_is_recorded_under_the_scan_and_the_list_is_still_read()
    {
        _system.ScanFailure = new Win32Exception(5, "WlanScan: Отказано в доступе");

        Reading reading = Read();

        List<string> networks = Section(reading, "Видимые сети");
        int scan = networks.IndexOf("      [Сканирование]");
        Assert.Equal("        ОШИБКА: System.ComponentModel.Win32Exception (код 5): WlanScan: Отказано в доступе", networks[scan + 2]);
        Assert.Contains("      Сетей: 2", networks);
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Failure_to_read_the_network_list_is_recorded_in_the_adapter_section()
    {
        // Так ManagedNativeWifi сообщает об ошибке 5 — отказе при выключенном доступе к местоположению
        _system.AvailableNetworksFailure = new UnauthorizedAccessException("MethodName: WlanGetAvailableNetworkList, ErrorCode: 5");

        Reading reading = Read();

        List<string> networks = Section(reading, "Видимые сети");
        Assert.Contains("      ОШИБКА: System.UnauthorizedAccessException (код 5): MethodName: WlanGetAvailableNetworkList, ErrorCode: 5", networks);
        Assert.DoesNotContain(networks, line => line.Contains("Сетей:"));
        Assert.Contains("      [Office]", Section(reading, "Профили сетей Windows"));
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Failure_to_read_the_bss_list_is_recorded_at_each_network()
    {
        _system.BssFailure = new Win32Exception(5, "WlanGetNetworkBssList: Отказано в доступе");

        Reading reading = Read();

        List<string> networks = Section(reading, "Видимые сети");
        Assert.Equal(2, networks.Count(line => line == "          ОШИБКА: System.ComponentModel.Win32Exception (код 5): WlanGetNetworkBssList: Отказано в доступе"));
        Assert.Contains("        С профилем сети Windows «Office»: сигнал 80 %, защита RSNA_PSK, CCMP", networks);
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Failure_to_read_the_bss_list_without_networks_is_recorded_in_the_adapter_section()
    {
        _system.AvailableNetworks.Clear();
        _system.BssFailure = new Win32Exception(5, "WlanGetNetworkBssList: Отказано в доступе");

        Reading reading = Read();

        List<string> networks = Section(reading, "Видимые сети");
        int count = networks.IndexOf("      Сетей: 0");
        Assert.Equal("      ОШИБКА: System.ComponentModel.Win32Exception (код 5): WlanGetNetworkBssList: Отказано в доступе", networks[count + 1]);
    }

    [Fact]
    public void Hidden_network_and_access_points_missing_from_the_network_list_are_shown()
    {
        _system.AvailableNetworks.Add(new("", "Infrastructure", false, 40, true, "", "RSNA_PSK", "CCMP"));
        _system.Bss.Add(new("", "50-FF-20-26-6E-10", 5745000, 5f, 149, -77));
        _system.Bss.Add(new("Keenetic-7212", "50-FF-20-26-6E-11", 2447000, 2.4f, 8, -80));

        Reading reading = Read();

        List<string> networks = Section(reading, "Видимые сети");
        int hidden = networks.IndexOf("      [(скрытая сеть — имя не передаётся)]");
        Assert.Equal(
            [
                "        Без профиля сети Windows: сигнал 40 %, защита RSNA_PSK, CCMP, подключиться нельзя",
                "        [Точки доступа]",
                "          50-FF-20-26-6E-10: 5745 МГц (5 ГГц, канал 149), -77 дБм",
                "      [Точки доступа без сети в списке]",
                "        50-FF-20-26-6E-11: Keenetic-7212, 2447 МГц (2.4 ГГц, канал 8), -80 дБм",
            ],
            networks.Skip(hidden + 1));
    }

    [Fact]
    public void Network_without_access_points_in_the_bss_list_is_marked()
    {
        _system.Bss.RemoveAll(bss => bss.Ssid == "Cafe");

        Reading reading = Read();

        List<string> networks = Section(reading, "Видимые сети");
        int cafe = networks.IndexOf("      [Cafe]");
        Assert.Equal(["        [Точки доступа]", "          Нет в списке BSS"], networks.Skip(cafe + 2).Take(2));
    }

    [Fact]
    public void Windows_profiles_are_shown_without_the_network_key()
    {
        Reading reading = Read();

        Assert.Equal(
            [
                "    [Intel(R) Wi-Fi 6 AX200 160MHz]",
                "      [Office]",
                "        SSID: Office",
                "        Тип: AllUser",
                "        Место в порядке предпочтения: 1",
                "        Режим подключения: auto — подключаться автоматически",
                "        Случайный MAC-адрес в XML: enableRandomization = false, randomizationSeed = 1921880196",
                "      [Дача]",
                "        SSID: Dacha-5G",
                "        Тип: AllUser",
                "        Место в порядке предпочтения: 2",
                "        Режим подключения: manual — только вручную",
                "        Случайный MAC-адрес в XML: элемента MacRandomization нет",
            ],
            Section(reading, "Профили сетей Windows"));
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Missing_elements_of_a_windows_profile_are_marked()
    {
        _system.WindowsProfiles = [new("Гости", "PerUser", 0, null, null, [])];

        Reading reading = Read();

        List<string> profiles = Section(reading, "Профили сетей Windows");
        Assert.Contains("        SSID: элемента SSID нет", profiles);
        Assert.Contains("        Режим подключения: элемента connectionMode нет", profiles);
        Assert.Contains("        Случайный MAC-адрес в XML: (пусто)", profiles);
    }

    [Fact]
    public void Adapter_without_windows_profiles_is_marked()
    {
        _system.WindowsProfiles.Clear();

        Reading reading = Read();

        Assert.Equal(["    [Intel(R) Wi-Fi 6 AX200 160MHz]", "      Профилей сетей Windows нет"], Section(reading, "Профили сетей Windows"));
    }

    [Fact]
    public void Failure_to_read_windows_profiles_is_recorded_in_their_section()
    {
        _system.WindowsProfilesFailure = new Win32Exception(1168, "WlanGetProfileList: Элемент не найден");

        Reading reading = Read();

        List<string> profiles = Section(reading, "Профили сетей Windows");
        Assert.Equal("      ОШИБКА: System.ComponentModel.Win32Exception (код 1168): WlanGetProfileList: Элемент не найден", profiles[1]);
        Assert.Contains("      Сетей: 2", Section(reading, "Видимые сети"));
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Networks_and_profiles_are_not_read_without_the_list_of_wifi_adapters()
    {
        _system.WifiAdaptersFailure = new Win32Exception(1062, "WlanEnumInterfaces: служба не запущена");

        Reading reading = Read();

        Assert.Equal(["    Не прочитано: нет списка Wi-Fi-адаптеров"], Section(reading, "Видимые сети"));
        Assert.Equal(["    Не прочитано: нет списка Wi-Fi-адаптеров"], Section(reading, "Профили сетей Windows"));
        Assert.Equal(0, _system.Scans);
    }
}
