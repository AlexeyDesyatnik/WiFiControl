using System.ComponentModel;
using WiFiControl.Recon.Diagnostics;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Tests;

public class ReconLogRouteTests
{
    private readonly FakeSystem _system = new();
    private readonly FakeClock _clock = new();

    private Reading Read() => new ReconLog(_system, _clock, "1.2.3-test").Read("при запуске");

    private static List<string> SectionLines(Reading reading)
    {
        int section = reading.Lines.ToList().IndexOf("  [Маршрут в интернет]");
        Assert.True(section >= 0, reading.Text);
        return reading.Lines.Skip(section + 1).TakeWhile(line => line.StartsWith("    ", StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public void Internet_goes_through_wifi_when_it_has_the_only_default_route()
    {
        Reading reading = Read();

        Assert.Equal(
            [
                "    Адрес назначения: 8.8.8.8 — маршрут выбирает GetBestRoute2 по таблице, трафик не отправляется",
                "    [Выбранный маршрут]",
                "      Адаптер: Беспроводная сеть (Intel(R) Wi-Fi 6 AX200 160MHz)",
                "      Сеть назначения: 0.0.0.0/0",
                "      Шлюз: 192.168.1.1",
                "      Адрес отправителя: 192.168.1.37",
                "      Метрика: 0 маршрута + 35 интерфейса = 35",
                "    [Маршруты по умолчанию]",
                "      [Беспроводная сеть (Intel(R) Wi-Fi 6 AX200 160MHz)]",
                "        Шлюз: 192.168.1.1",
                "        Выбран: да",
                "        Метрика: 0 маршрута + 35 интерфейса = 35",
            ],
            SectionLines(reading));
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Vpn_route_narrower_than_the_default_one_is_shown_as_chosen()
    {
        var vpnId = new Guid("9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d");
        _system.Adapters.Add(new(vpnId, "tun2", "tun2socks Tunnel", @"ROOT\NET\0001", "", false, AdapterState.Connected));
        _system.IpInterfaces.Add(new(vpnId, 47, 5, true));
        _system.BestRoutes["8.8.8.8"] = new(new(vpnId, 47, "8.0.0.0/5", "0.0.0.0", 1), "10.33.0.2");

        Reading reading = Read();

        List<string> lines = SectionLines(reading);
        Assert.Equal(
            [
                "    [Выбранный маршрут]",
                "      Адаптер: tun2 (tun2socks Tunnel)",
                "      Сеть назначения: 8.0.0.0/5",
                "      Шлюз: 0.0.0.0",
                "      Адрес отправителя: 10.33.0.2",
                "      Метрика: 1 маршрута + 5 интерфейса = 6",
            ],
            lines.Skip(1).Take(6));
        Assert.Contains("        Выбран: нет", lines);
    }

    [Fact]
    public void Internet_goes_through_ethernet_when_its_metric_is_lower()
    {
        var ethernet = new Route(FakeSystem.EthernetId, 12, "0.0.0.0/0", "10.68.12.1", 0);
        _system.DefaultRoutes.Insert(0, ethernet);
        _system.BestRoutes["8.8.8.8"] = new(ethernet, "10.68.12.37");

        Reading reading = Read();

        Assert.Equal(
            [
                "      Адаптер: Ethernet (Realtek PCIe GbE Family Controller)",
                "      Сеть назначения: 0.0.0.0/0",
                "      Шлюз: 10.68.12.1",
                "      Адрес отправителя: 10.68.12.37",
                "      Метрика: 0 маршрута + 25 интерфейса = 25",
                "    [Маршруты по умолчанию]",
                "      [Ethernet (Realtek PCIe GbE Family Controller)]",
                "        Шлюз: 10.68.12.1",
                "        Выбран: да",
                "        Метрика: 0 маршрута + 25 интерфейса = 25",
                "      [Беспроводная сеть (Intel(R) Wi-Fi 6 AX200 160MHz)]",
                "        Шлюз: 192.168.1.1",
                "        Выбран: нет",
                "        Метрика: 0 маршрута + 35 интерфейса = 35",
            ],
            SectionLines(reading).Skip(2));
    }

    [Fact]
    public void Computer_without_a_network_has_no_route_to_the_internet()
    {
        _system.BestRoutes.Clear();
        _system.DefaultRoutes.Clear();

        Reading reading = Read();

        Assert.Equal(
            [
                "    [Выбранный маршрут]",
                "      Маршрута нет",
                "    [Маршруты по умолчанию]",
                "      Маршрутов по умолчанию нет",
            ],
            SectionLines(reading).Skip(1));
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Failure_of_best_route_is_recorded_with_its_code_and_default_routes_are_still_listed()
    {
        _system.BestRouteFailure = new Win32Exception(1168, "GetBestRoute2(8.8.8.8): Элемент не найден.");

        Reading reading = Read();

        List<string> lines = SectionLines(reading);
        Assert.Equal("      ОШИБКА: System.ComponentModel.Win32Exception (код 1168): GetBestRoute2(8.8.8.8): Элемент не найден.", lines[2]);
        Assert.Contains("        Выбран: не определить — выбранный маршрут не прочитан", lines);
        Assert.Contains("        Шлюз: 192.168.1.1", lines);
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Failure_to_read_interface_metrics_keeps_the_rest_of_the_chosen_route()
    {
        _system.IpInterfacesFailure = new Win32Exception(87, "GetIpInterfaceTable: Параметр задан неверно.");

        Reading reading = Read();

        List<string> lines = SectionLines(reading);
        Assert.Equal("      Адрес отправителя: 192.168.1.37", lines[5]);
        Assert.Equal("      ОШИБКА: System.ComponentModel.Win32Exception (код 87): GetIpInterfaceTable: Параметр задан неверно.", lines[6]);
        Assert.Contains("        Выбран: да", lines);
    }

    [Fact]
    public void Failure_to_read_default_routes_is_recorded_with_its_code_and_keeps_the_chosen_route()
    {
        _system.DefaultRoutesFailure = new Win32Exception(8, "GetIpForwardTable2: Недостаточно памяти.");

        Reading reading = Read();

        List<string> lines = SectionLines(reading);
        int routes = lines.IndexOf("    [Маршруты по умолчанию]");
        Assert.Equal("      ОШИБКА: System.ComponentModel.Win32Exception (код 8): GetIpForwardTable2: Недостаточно памяти.", lines[routes + 1]);
        Assert.Contains("      Шлюз: 192.168.1.1", lines);
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Route_of_an_unknown_adapter_and_interface_is_shown_by_its_interface_number()
    {
        _system.DefaultRoutes.Add(new(new Guid("3f2a9c10-5b6d-4e7f-8a9b-0c1d2e3f4a5b"), 31, "0.0.0.0/0", "172.16.0.1", 5));

        Reading reading = Read();

        Assert.Equal(
            [
                "      [интерфейс 31, {3f2a9c10-5b6d-4e7f-8a9b-0c1d2e3f4a5b} — в «Сетевых подключениях» такого адаптера нет]",
                "        Шлюз: 172.16.0.1",
                "        Выбран: нет",
                "        Метрика: 5 маршрута, метрики интерфейса нет — нет интерфейса IPv4 с номером 31",
            ],
            SectionLines(reading).TakeLast(4));
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Routes_are_shown_by_interface_number_when_adapters_could_not_be_read()
    {
        _system.AdaptersFailure = new InvalidOperationException("WMI недоступна");

        Reading reading = Read();

        List<string> lines = SectionLines(reading);
        Assert.Contains("      Адаптер: интерфейс 18, {" + FakeSystem.WifiId + "} — адаптеры не прочитаны", lines);
        Assert.Contains("        Метрика: 0 маршрута + 35 интерфейса = 35", lines);
        Assert.DoesNotContain(lines, line => line.Contains("ОШИБКА"));
    }
}
