using System.Text;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

/// <summary>Переключение по ADR 0004 и откат, когда подключиться не удалось.</summary>
public class SwitchTests
{
    private const string Disconnect = "Отключение от сети «Home»";
    private const string Ipv4 = "Настройки IPv4 адаптера «Беспроводная сеть»";
    private const string Connect = "Подключение к сети «Office»";
    private const string Proxy = "Настройки прокси";
    private const string Ipv4Rollback = "Откат настроек IPv4";
    private const string Return = "Возврат в сеть «Home»";

    private readonly RunningClock _time = new();
    private readonly FakeSystem _system;
    private readonly FakeWifi _wifi = new();
    private readonly Applier _applier;
    private readonly Adapter _adapter = FakeSystem.WiFi(AdapterState.Connected);
    private readonly Ipv4Settings _homeIpv4 = FakeSystem.Static("192.168.1.37", "192.168.1.1", "192.168.1.1");
    private readonly Profile _office = new() { Name = "Офис", Ipv4 = FakeSystem.Static("10.68.12.37", "10.68.12.1", "10.68.0.10"),
        Proxy = ProxySettings.FromRaw(true, "10.0.68.52:3128", "<local>") };

    public SwitchTests()
    {
        _system = new FakeSystem(_time);
        // Адаптер в сети Home; после подключения к Office адрес, маршрут, шлюз и прокси в порядке
        _adapter.Configured = _homeIpv4.Clone();
        _adapter.Active.Addresses.Add("10.68.12.37/24");
        _adapter.Active.Gateways.Add("10.68.12.1");
        _system.Adapters.Add(_adapter);
        _system.DefaultRoutes.Add(new DefaultRoute(FakeWifi.WifiId, "10.68.12.1"));
        _system.Answering.Add("10.68.12.1");
        _system.OpenPorts.Add("10.0.68.52:3128");

        _wifi.WindowsProfiles.Add(new WindowsProfile("Home", "Home"));
        _wifi.WindowsProfiles.Add(new WindowsProfile("Office", "Office"));
        _wifi.Current = Encoding.UTF8.GetBytes("Home");
        _applier = new Applier(_system, _wifi, _time);
    }

    private Task<IReadOnlyList<StepResult>> SwitchAsync(Profile? profile = null, Action<StepResult>? onStep = null)
    {
        return _applier.SwitchAsync(_adapter, "Office", "Office", profile ?? _office, onStep, CancellationToken.None);
    }

    private static StepResult Step(IReadOnlyList<StepResult> results, string title) => results.Single(result => result.Title == title);

    private string? Current => _wifi.Current == null ? null : NetworkKey.FromBytes(_wifi.Current);

    [Fact]
    public async Task Switch_goes_disconnect_ipv4_connect_proxy_check_and_becomes_the_last_application()
    {
        var reported = new List<StepResult>();

        IReadOnlyList<StepResult> results = await SwitchAsync(onStep: reported.Add);

        Assert.Equal([Disconnect, Ipv4, Connect, Proxy, "Маршрут по умолчанию", "Адрес", "Шлюз", "Порт прокси"], results.Select(result => result.Title));
        Assert.All(results, result => Assert.Equal(StepOutcome.Done, result.Outcome));
        Assert.Equal(results, reported);
        Assert.Equal(["disconnect", "connect Office"], _wifi.Calls);
        Assert.Equal("Office", Current);
        Assert.True(_adapter.Configured.SameAs(_office.Ipv4));
        Assert.True(_system.SystemProxy.Settings.Enabled);
        Assert.Equal(new LastApplication("Office", _office.Id), _applier.Last);
    }

    [Fact]
    public async Task Without_a_previous_network_disconnecting_is_skipped()
    {
        _wifi.Current = null;

        IReadOnlyList<StepResult> results = await SwitchAsync();

        Assert.Equal([Ipv4, Connect, Proxy], results.Take(3).Select(result => result.Title));
        Assert.Equal(["connect Office"], _wifi.Calls);
        Assert.NotNull(_applier.Last);
    }

    [Fact]
    public async Task No_word_from_windows_within_45_seconds_is_a_failure()
    {
        _wifi.Silent.Add("Office");

        IReadOnlyList<StepResult> results = await SwitchAsync();

        StepResult connect = Step(results, Connect);
        Assert.Equal(StepOutcome.Error, connect.Outcome);
        Assert.Equal("Windows не подключилась за 45 с.", connect.Message);
        Assert.True(_time.Elapsed >= TimeSpan.FromSeconds(45));
        // Попытка прерывается, чтобы не закончилась после возврата
        Assert.Equal(["disconnect", "connect Office", "disconnect", "connect Home"], _wifi.Calls);
        Assert.Null(_applier.Last);
    }

    [Fact]
    public async Task Failure_notification_starts_the_rollback_at_once_with_the_reason_from_the_attempt()
    {
        _wifi.Failing["Office"] = WlanReason.PskMismatchSuspected;

        IReadOnlyList<StepResult> results = await SwitchAsync();

        StepResult connect = Step(results, Connect);
        Assert.Equal(StepOutcome.Error, connect.Outcome);
        Assert.Equal("Похоже, пароль неверный: Windows не прошла проверку подлинности.", connect.Message);
        Assert.True(_time.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal([Disconnect, Ipv4, Connect, Ipv4Rollback, Return], results.Select(result => result.Title));
        Assert.Null(_applier.Last);
    }

    [Fact]
    public async Task Windows_refusing_to_start_connecting_is_a_failure_too()
    {
        _wifi.ConnectFailure = new InvalidOperationException("Windows не начала подключение: отказ.");

        IReadOnlyList<StepResult> results = await SwitchAsync();

        Assert.Equal("Windows не начала подключение: отказ.", Step(results, Connect).Error);
        Assert.Equal(StepOutcome.Done, Step(results, Ipv4Rollback).Outcome);
    }

    [Fact]
    public async Task Rollback_returns_the_ipv4_snapshot()
    {
        _wifi.Failing["Office"] = WlanReason.AssociationFailure;

        IReadOnlyList<StepResult> results = await SwitchAsync();

        Assert.Equal(StepOutcome.Done, Step(results, Ipv4Rollback).Outcome);
        Assert.True(_adapter.Configured.SameAs(_homeIpv4));
        Assert.Equal(["ipv4 Беспроводная сеть", "ipv4 Беспроводная сеть"], _system.Calls);
    }

    [Fact]
    public async Task Proxy_is_not_written_and_nothing_is_checked_when_connecting_failed()
    {
        _wifi.Failing["Office"] = WlanReason.AssociationFailure;

        IReadOnlyList<StepResult> results = await SwitchAsync();

        Assert.DoesNotContain(results, result => result.Title == Proxy);
        Assert.DoesNotContain(_system.Calls, call => call is "system proxy" or "winhttp" || call.StartsWith("ping"));
        Assert.False(_system.SystemProxy.Settings.Enabled);
    }

    [Fact]
    public async Task Rollback_reconnects_to_the_previous_network_through_its_windows_profile()
    {
        _wifi.Failing["Office"] = WlanReason.AssociationFailure;

        IReadOnlyList<StepResult> results = await SwitchAsync();

        Assert.Equal(StepOutcome.Done, Step(results, Return).Outcome);
        Assert.Equal(["disconnect", "connect Office", "connect Home"], _wifi.Calls);
        Assert.Equal("Home", Current);
    }

    [Fact]
    public async Task Failed_return_shows_both_errors_and_does_nothing_more()
    {
        _wifi.Failing["Office"] = WlanReason.PskMismatchSuspected;
        _wifi.Failing["Home"] = WlanReason.NoVisibleAccessPoint;

        IReadOnlyList<StepResult> results = await SwitchAsync();

        Assert.Equal([Disconnect, Ipv4, Connect, Ipv4Rollback, Return], results.Select(result => result.Title));
        Assert.Equal([Connect, Return], results.Where(result => !result.Ok).Select(result => result.Title));
        Assert.Equal("Сеть не отвечает: точка доступа вне зоны или выключена.", Step(results, Return).Error);
        Assert.Null(Current);
    }

    [Fact]
    public async Task Without_a_previous_network_rollback_only_returns_ipv4()
    {
        _wifi.Current = null;
        _wifi.Failing["Office"] = WlanReason.AssociationFailure;

        IReadOnlyList<StepResult> results = await SwitchAsync();

        Assert.Equal([Ipv4, Connect, Ipv4Rollback], results.Select(result => result.Title));
        Assert.Equal(["connect Office"], _wifi.Calls);
        Assert.True(_adapter.Configured.SameAs(_homeIpv4));
    }

    [Fact]
    public async Task Notifications_about_another_network_or_adapter_do_not_decide_the_outcome()
    {
        _wifi.Silent.Add("Office");
        _wifi.DuringConnect.Add(new WifiConnected(FakeWifi.WifiId, "Home"));
        _wifi.DuringConnect.Add(new WifiConnected(Guid.NewGuid(), "Office"));
        _wifi.DuringConnect.Add(new WifiConnectionFailed(FakeWifi.WifiId, "Home", WlanReason.NetworkNotAvailable));

        IReadOnlyList<StepResult> results = await SwitchAsync();

        Assert.Equal("Windows не подключилась за 45 с.", Step(results, Connect).Error);
    }

    [Fact]
    public async Task No_address_after_connecting_is_a_warning_not_a_rollback()
    {
        _adapter.Active.Addresses.Clear();
        _system.DefaultRoutes.Clear();
        _adapter.Active.Gateways.Clear();

        IReadOnlyList<StepResult> results = await SwitchAsync(Profile.Automatic);

        Assert.Equal(StepOutcome.Warning, Step(results, "Адрес").Outcome);
        Assert.DoesNotContain(results, result => result.Title is Ipv4Rollback or Return);
        Assert.Equal(new LastApplication("Office", Profile.Automatic.Id), _applier.Last);
    }

    [Fact]
    public async Task Unread_current_network_means_the_switch_does_not_start()
    {
        _wifi.CurrentFailure = new InvalidOperationException("Windows не отдала имя текущей сети: отказ.");

        IReadOnlyList<StepResult> results = await SwitchAsync();

        StepResult only = Assert.Single(results);
        Assert.Equal("Переключение не начато. Windows не отдала имя текущей сети: отказ.", only.Error);
        Assert.Empty(_wifi.Calls);
        Assert.Empty(_system.Calls);
    }
}
