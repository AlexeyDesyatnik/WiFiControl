using System.ComponentModel;
using System.Management;
using WiFiControl.Recon.Diagnostics;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Tests;

public class ReconLogWiredTests
{
    private readonly FakeSystem _system = new();
    private readonly FakeClock _clock = new();

    private Reading Read() => new ReconLog(_system, _clock, "1.2.3-test").Read("при запуске");

    private static List<string> SectionLines(Reading reading, int count)
    {
        int section = reading.Lines.ToList().IndexOf("  [Проводные адаптеры]");
        Assert.True(section >= 0, reading.Text);
        return reading.Lines.Skip(section + 1).Take(count).ToList();
    }

    private void SetEthernet(Func<Adapter, Adapter> change)
    {
        int index = _system.Adapters.FindIndex(a => a.Id == FakeSystem.EthernetId);
        _system.Adapters[index] = change(_system.Adapters[index]);
    }

    [Fact]
    public void Wired_adapter_without_cable_shows_state_and_interface_metric()
    {
        Reading reading = Read();

        Assert.Equal(
            [
                "    [Ethernet]",
                "      Описание: Realtek PCIe GbE Family Controller",
                "      Состояние: NoLink",
                "      Шлюзы: не прочитаны — нет соединения",
                "      Метрика интерфейса: 25, назначена автоматически",
            ],
            SectionLines(reading, 5));
        Assert.False(reading.HasErrors, reading.Text);
    }

    [Fact]
    public void Connected_wired_adapter_shows_its_gateways_and_manual_metric()
    {
        SetEthernet(a => a with { State = AdapterState.Connected, Active = new(["10.68.12.37/24"], ["10.68.12.1"], ["10.68.12.1"], false) });
        _system.IpInterfaces[0] = new(FakeSystem.EthernetId, 12, 5, false);

        Reading reading = Read();

        Assert.Equal(
            [
                "      Состояние: Connected",
                "      Шлюзы: 10.68.12.1",
                "      Метрика интерфейса: 5, задана вручную",
            ],
            SectionLines(reading, 5).Skip(2));
    }

    [Fact]
    public void Wifi_and_virtual_adapters_are_not_listed_as_wired()
    {
        _system.Adapters.Add(new(new Guid("7e3a1f20-0c4d-4b7a-a1e2-9f0d3c5b6a71"), "vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter",
            @"ROOT\VMS_MP\0000", "00-15-5D-01-02-03", false, AdapterState.Connected));
        _system.Adapters.RemoveAll(a => a.Id == FakeSystem.EthernetId);

        Reading reading = Read();

        Assert.Equal(["    Проводных адаптеров нет"], SectionLines(reading, 1));
    }

    [Fact]
    public void Disabled_adapter_is_listed_with_a_note_that_its_kind_is_unknown()
    {
        SetEthernet(a => a with { State = AdapterState.Disabled });
        _system.IpInterfaces.RemoveAll(i => i.AdapterId == FakeSystem.EthernetId);

        Reading reading = Read();

        Assert.Equal(
            [
                "    [Ethernet]",
                "      Описание: Realtek PCIe GbE Family Controller",
                "      Состояние: Disabled",
                "      Тип: не определить — у выключенного адаптера Windows не сообщает, Wi-Fi он или проводной",
                "      Шлюзы: не прочитаны — нет соединения",
                "      Метрика интерфейса: нет — у адаптера нет интерфейса IPv4",
            ],
            SectionLines(reading, 6));
    }

    [Fact]
    public void Failure_to_read_interface_metrics_is_recorded_with_its_code_and_keeps_the_state()
    {
        _system.IpInterfacesFailure = new Win32Exception(87, "GetIpInterfaceTable: Параметр задан неверно.");

        Reading reading = Read();

        List<string> lines = SectionLines(reading, 5);
        Assert.Equal("      Состояние: NoLink", lines[2]);
        Assert.Equal("      ОШИБКА: System.ComponentModel.Win32Exception (код 87): GetIpInterfaceTable: Параметр задан неверно.", lines[4]);
        Assert.True(reading.HasErrors);
    }

    [Fact]
    public void Failure_to_read_adapters_is_recorded_in_the_wired_section()
    {
        _system.AdaptersFailure = new ManagementException("Недопустимый запрос");

        Reading reading = Read();

        Assert.StartsWith("    ОШИБКА: System.Management.ManagementException", SectionLines(reading, 1)[0]);
    }
}
