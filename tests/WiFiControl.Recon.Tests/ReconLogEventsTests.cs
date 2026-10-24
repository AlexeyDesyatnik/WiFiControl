using System.ComponentModel;
using WiFiControl.Recon.Diagnostics;
using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Tests;

public class ReconLogEventsTests
{
    private readonly FakeSystem _system = new();
    private readonly FakeClock _clock = new();
    private readonly ReconLog _log;

    public ReconLogEventsTests()
    {
        _log = new ReconLog(_system, _clock, "1.2.3-test");
    }

    private void RaiseAt(int hour, int minute, int second, int millisecond, SystemEvent systemEvent)
    {
        _clock.Now = new DateTime(2026, 10, 23, hour, minute, second, millisecond);
        _system.Raise(systemEvent);
    }

    [Fact]
    public void Events_go_into_the_feed_in_order_with_the_time_of_the_clock()
    {
        _log.Read("при запуске");
        using IDisposable watching = _log.StartWatching();

        RaiseAt(9, 20, 1, 5, new WlanNotification(FakeSystem.WifiId, "ACM", 9, "connection_start", "Office", "Office"));
        RaiseAt(9, 20, 1, 912, new WlanNotification(FakeSystem.WifiId, "MSM", 4, "connected", "Office", "Office", "CC-2D-21-3B-83-96"));
        RaiseAt(9, 20, 2, 40, new WlanNotification(FakeSystem.WifiId, "ACM", 10, "connection_complete", "Office", "Office"));
        RaiseAt(9, 20, 4, 377, new AddressesChanged([new(FakeSystem.WifiId, "Беспроводная сеть", "Up",
            new(["192.168.1.37/24"], ["192.168.1.1"], ["192.168.1.1"], false))]));

        Assert.Equal(
            [
                "2026-10-23 09:20:01.005  WLAN ACM connection_start (9), Intel(R) Wi-Fi 6 AX200 160MHz, SSID «Office», профиль сети Windows «Office»",
                "2026-10-23 09:20:01.912  WLAN MSM connected (4), Intel(R) Wi-Fi 6 AX200 160MHz, SSID «Office», профиль сети Windows «Office», BSSID CC-2D-21-3B-83-96",
                "2026-10-23 09:20:02.040  WLAN ACM connection_complete (10), Intel(R) Wi-Fi 6 AX200 160MHz, SSID «Office», профиль сети Windows «Office»",
                "2026-10-23 09:20:04.377  Смена адресов",
                "  Беспроводная сеть (Up): адреса 192.168.1.37/24; шлюзы 192.168.1.1; DNS 192.168.1.1",
            ],
            _log.Events.SelectMany(e => e.Lines));
        Assert.Equal([1, 2, 3, 4], _log.Events.Select(e => e.Number));
    }

    [Fact]
    public void Signal_quality_and_failure_reason_are_shown()
    {
        _log.Read("при запуске");
        using IDisposable watching = _log.StartWatching();

        _system.Raise(new WlanNotification(FakeSystem.WifiId, "MSM", 8, "signal_quality_change", SignalQuality: 64));
        _system.Raise(new WlanNotification(FakeSystem.WifiId, "ACM", 11, "connection_attempt_fail", "Cafe", "Cafe",
            ReasonCode: 229377, Reason: "Сеть недоступна."));

        Assert.EndsWith("WLAN MSM signal_quality_change (8), Intel(R) Wi-Fi 6 AX200 160MHz, сигнал 64 %", _log.Events[0].Text);
        Assert.EndsWith("SSID «Cafe», профиль сети Windows «Cafe», причина 229377 — Сеть недоступна.", _log.Events[1].Text);
    }

    [Fact]
    public void Adapter_unknown_to_the_readings_is_shown_by_guid()
    {
        using IDisposable watching = _log.StartWatching();

        _system.Raise(new WlanNotification(FakeSystem.WifiId, "ACM", 7, "scan_complete"));

        Assert.Equal("2026-10-23 09:15:42.317  WLAN ACM scan_complete (7), {b0fbe93a-b7d1-490e-9b93-c44d06b82760}", _log.Events.Single().Text);
    }

    [Fact]
    public void Address_change_without_wifi_adapters_and_without_addresses_is_marked()
    {
        using IDisposable watching = _log.StartWatching();

        _system.Raise(new AddressesChanged([]));
        _system.Raise(new AddressesChanged([new(FakeSystem.WifiId, "Беспроводная сеть", "Down", new([], [], [], false))]));

        Assert.Equal("  Wi-Fi-адаптеров нет", _log.Events[0].Lines[1]);
        Assert.Equal("  Беспроводная сеть (Down): адреса (пусто); шлюзы (пусто); DNS (пусто)", _log.Events[1].Lines[1]);
    }

    [Fact]
    public void Sleep_and_wake_are_recorded()
    {
        using IDisposable watching = _log.StartWatching();

        RaiseAt(23, 1, 0, 0, new PowerModeChanged("Suspend"));
        RaiseAt(7, 30, 12, 250, new PowerModeChanged("Resume"));

        Assert.Equal(
            ["2026-10-23 23:01:00.000  Питание: Suspend — уход в сон", "2026-10-23 07:30:12.250  Питание: Resume — выход из сна"],
            _log.Events.Select(e => e.Text));
    }

    [Fact]
    public void Failure_of_a_subscription_is_recorded_with_its_code()
    {
        using IDisposable watching = _log.StartWatching();

        _system.Raise(new EventFailure("Подписка на уведомления WLAN", new Win32Exception(1062, "WlanOpenHandle: служба не запущена")));

        Assert.Equal(
            ["2026-10-23 09:15:42.317  Не удалось: Подписка на уведомления WLAN",
             "  ОШИБКА: System.ComponentModel.Win32Exception (код 1062): WlanOpenHandle: служба не запущена"],
            _log.Events.Single().Lines);
    }

    [Fact]
    public void Feed_goes_into_the_report_after_the_readings()
    {
        using IDisposable watching = _log.StartWatching();
        _log.Read("при запуске");
        _system.Raise(new PowerModeChanged("Resume"));
        _log.Read("по кнопке «Прочитать заново»");

        List<string> lines = _log.BuildReport().Lines.ToList();

        int feed = lines.IndexOf("== Лента событий ==");
        Assert.True(feed > lines.FindLastIndex(line => line.StartsWith("== Чтение ", StringComparison.Ordinal)), string.Join("\n", lines));
        Assert.Equal(["  Событий: 1", "  2026-10-23 09:15:42.317  Питание: Resume — выход из сна"], lines.Skip(feed + 1));
    }

    [Fact]
    public void New_events_are_announced()
    {
        var announced = new List<RecordedEvent>();
        _log.EventRecorded += announced.Add;
        using IDisposable watching = _log.StartWatching();

        _system.Raise(new PowerModeChanged("Suspend"));

        Assert.Equal(_log.Events, announced);
    }

    [Fact]
    public void Disposing_the_watch_removes_the_subscription()
    {
        IDisposable watching = _log.StartWatching();
        Assert.True(_system.Watching);

        watching.Dispose();
        _system.Raise(new PowerModeChanged("Suspend"));

        Assert.False(_system.Watching);
        Assert.Empty(_log.Events);
    }
}
