using System.ComponentModel;
using System.Net.NetworkInformation;
using WiFiControl.Recon.Diagnostics;

namespace WiFiControl.Recon.Tests;

public class ReconLogWifiTests
{
    private readonly FakeSystem _system = new();
    private readonly FakeClock _clock = new();

    private Reading Read() => new ReconLog(_system, _clock, "1.2.3-test").Read("при запуске");

    [Fact]
    public void Wifi_adapter_is_shown_with_its_network_connection()
    {
        Reading reading = Read();

        int adapters = reading.Lines.ToList().IndexOf("  [Wi-Fi-адаптеры]");
        Assert.True(adapters >= 0, reading.Text);
        Assert.Equal(
            [
                "    [Intel(R) Wi-Fi 6 AX200 160MHz]",
                "      GUID: b0fbe93a-b7d1-490e-9b93-c44d06b82760",
                "      Описание: Intel(R) Wi-Fi 6 AX200 160MHz",
                "      Состояние: Connected",
                "      Сетевое подключение: Беспроводная сеть (Intel(R) Wi-Fi 6 AX200 160MHz), Up",
            ],
            reading.Lines.Skip(adapters + 1).Take(5));
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Wifi_adapter_missing_from_network_connections_is_marked()
    {
        _system.NetworkConnections.RemoveAll(c => c.Id == FakeSystem.WifiId);

        Reading reading = Read();

        Assert.Contains("      Сетевое подключение: нет — в «Сетевых подключениях» нет адаптера с таким GUID", reading.Lines);
    }

    [Fact]
    public void Current_connection_is_shown_for_each_wifi_adapter()
    {
        Reading reading = Read();

        int connection = reading.Lines.ToList().IndexOf("  [Текущее подключение]");
        Assert.True(connection >= 0, reading.Text);
        Assert.Equal(
            [
                "    [Intel(R) Wi-Fi 6 AX200 160MHz]",
                "      SSID: Office",
                "      BSSID: CC-2D-21-3B-83-96",
                "      Профиль сети Windows: Office",
                "      Режим подключения: Profile",
                "      Защита включена: да",
                "      Тип защиты: RSNA_PSK, CCMP",
                "      Сигнал: 82 %",
                "      Частота: 5180 МГц (5 ГГц, канал 36)",
                "      RSSI: -61 дБм",
            ],
            reading.Lines.Skip(connection + 1).Take(10));
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Wifi_adapter_without_connection_is_marked()
    {
        _system.Connections.Clear();

        Reading reading = Read();

        int connection = reading.Lines.ToList().IndexOf("  [Текущее подключение]");
        Assert.Equal(["    [Intel(R) Wi-Fi 6 AX200 160MHz]", "      Не подключён"], reading.Lines.Skip(connection + 1).Take(2));
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Frequency_is_marked_when_the_access_point_is_missing_from_the_bss_list()
    {
        _system.Bss.Clear();

        Reading reading = Read();

        Assert.Contains("      Частота: нет — точки доступа с этим BSSID нет в списке BSS", reading.Lines);
        Assert.DoesNotContain(reading.Lines, line => line.Contains("RSSI"));
        Assert.Contains("      Сигнал: 82 %", reading.Lines);
    }

    [Fact]
    public void Failure_to_enumerate_wifi_adapters_is_recorded_in_their_section()
    {
        _system.WifiAdaptersFailure = new Win32Exception(1062, "WlanEnumInterfaces: служба не запущена");

        Reading reading = Read();

        int adapters = reading.Lines.ToList().IndexOf("  [Wi-Fi-адаптеры]");
        int connection = reading.Lines.ToList().IndexOf("  [Текущее подключение]");
        Assert.Equal("    ОШИБКА: System.ComponentModel.Win32Exception (код 1062): WlanEnumInterfaces: служба не запущена", reading.Lines[adapters + 1]);
        Assert.Equal("    Не прочитано: нет списка Wi-Fi-адаптеров", reading.Lines[connection + 1]);
        Assert.Contains("      Права администратора: да", reading.Lines);
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Failure_to_read_network_connections_is_recorded_in_the_adapter_section()
    {
        _system.NetworkConnectionsFailure = new NetworkInformationException(5);

        Reading reading = Read();

        int adapter = reading.Lines.ToList().IndexOf("    [Intel(R) Wi-Fi 6 AX200 160MHz]");
        Assert.Equal("      Состояние: Connected", reading.Lines[adapter + 3]);
        Assert.StartsWith("      ОШИБКА: System.Net.NetworkInformation.NetworkInformationException (код 5)", reading.Lines[adapter + 4]);
        Assert.Contains("      SSID: Office", reading.Lines);
    }

    [Fact]
    public void Failure_to_read_current_connection_is_recorded_in_its_section()
    {
        // Так ManagedNativeWifi сообщает об ошибке 5 — отказе при выключенном доступе к местоположению
        _system.ConnectionFailure = new UnauthorizedAccessException("MethodName: WlanQueryInterface, ErrorCode: 5");

        Reading reading = Read();

        int connection = reading.Lines.ToList().IndexOf("  [Текущее подключение]");
        Assert.Equal(
            ["    [Intel(R) Wi-Fi 6 AX200 160MHz]", "      ОШИБКА: System.UnauthorizedAccessException (код 5): MethodName: WlanQueryInterface, ErrorCode: 5"],
            reading.Lines.Skip(connection + 1).Take(2));
        Assert.Contains("      Состояние: Connected", reading.Lines);
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Failure_to_read_the_bss_list_is_recorded_after_the_connection_data()
    {
        _system.BssFailure = new Win32Exception(5, "WlanGetNetworkBssList: Отказано в доступе");

        Reading reading = Read();

        int signal = reading.Lines.ToList().IndexOf("      Сигнал: 82 %");
        Assert.Equal("      ОШИБКА: System.ComponentModel.Win32Exception (код 5): WlanGetNetworkBssList: Отказано в доступе", reading.Lines[signal + 1]);
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Computer_without_wifi_adapters_is_marked()
    {
        _system.WifiAdapters.Clear();

        Reading reading = Read();

        int adapters = reading.Lines.ToList().IndexOf("  [Wi-Fi-адаптеры]");
        int connection = reading.Lines.ToList().IndexOf("  [Текущее подключение]");
        Assert.Equal("    Wi-Fi-адаптеров нет", reading.Lines[adapters + 1]);
        Assert.Equal("    Wi-Fi-адаптеров нет", reading.Lines[connection + 1]);
        Assert.False(reading.HasErrors, reading.Text);
    }
}
