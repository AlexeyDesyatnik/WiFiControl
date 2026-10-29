using System.Text;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class ApplierTests
{
    private static readonly Guid WifiGuid = new("00000000-0000-0000-0000-000000000002");

    private readonly RunningClock _time = new();
    private readonly FakeSystem _system;
    private readonly Applier _applier;
    private readonly Adapter _wifi = FakeSystem.WiFi(AdapterState.Connected);
    private readonly Profile _office = new() { Name = "Офис", Ipv4 = FakeSystem.Static("10.68.12.37", "10.68.12.1", "10.68.0.10"),
        Proxy = ProxySettings.FromRaw(true, "10.0.68.52:3128", "<local>") };

    public ApplierTests()
    {
        _system = new FakeSystem(_time);
        // Подключённый адаптер с рабочим адресом и маршрутом, шлюз и прокси отвечают
        _wifi.Active.Addresses.Add("10.68.12.37/24");
        _wifi.Active.Gateways.Add("10.68.12.1");
        _system.Adapters.Add(_wifi);
        _system.DefaultRoutes.Add(new DefaultRoute(WifiGuid, "10.68.12.1"));
        _system.Answering.Add("10.68.12.1");
        _system.OpenPorts.Add("10.0.68.52:3128");
        _applier = new Applier(_system, new FakeWifi(), _time);
    }

    private Task<IReadOnlyList<StepResult>> ApplyAsync(Profile? profile = null, Action<StepResult>? onStep = null)
    {
        return _applier.ApplyProfileAsync(_wifi, "Office", profile ?? _office, onStep, CancellationToken.None);
    }

    [Fact]
    public async Task Profile_is_applied_as_ipv4_then_proxy_then_check_without_reconnecting()
    {
        var reported = new List<StepResult>();

        IReadOnlyList<StepResult> results = await ApplyAsync(onStep: reported.Add);

        Assert.Equal(["Настройки IPv4 адаптера «Беспроводная сеть»", "Настройки прокси", "Маршрут по умолчанию", "Адрес", "Шлюз", "Порт прокси"],
            results.Select(result => result.Title));
        Assert.All(results, result => Assert.Equal(StepOutcome.Done, result.Outcome));
        Assert.Equal(results, reported);
        Assert.Equal(["ipv4 Беспроводная сеть", "system proxy", "winhttp", "ping 10.68.12.1", "connect 10.0.68.52:3128"], _system.Calls);
        Assert.True(_wifi.Configured.SameAs(_office.Ipv4));
    }

    private static StepResult Step(IReadOnlyList<StepResult> results, string title) => results.Single(result => result.Title == title);

    [Fact]
    public async Task Missing_default_route_is_added_after_about_three_seconds()
    {
        _system.DefaultRoutes.Clear();

        IReadOnlyList<StepResult> results = await ApplyAsync();

        StepResult route = Step(results, "Маршрут по умолчанию");
        Assert.Equal(StepOutcome.Done, route.Outcome);
        Assert.Equal("Маршрута не было, добавлен через 10.68.12.1.", route.Message);
        Assert.Contains("add route 10.68.12.1", _system.Calls);
        Assert.Contains(new DefaultRoute(WifiGuid, "10.68.12.1"), _system.DefaultRoutes);
        Assert.InRange(_time.Elapsed, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task Default_route_that_appears_in_time_is_left_alone()
    {
        _system.DefaultRoutes.Clear();
        _system.Later(TimeSpan.FromSeconds(1.5), () => _system.DefaultRoutes.Add(new DefaultRoute(WifiGuid, "10.68.12.1")));

        IReadOnlyList<StepResult> results = await ApplyAsync();

        Assert.Equal(StepOutcome.Done, Step(results, "Маршрут по умолчанию").Outcome);
        Assert.DoesNotContain("add route 10.68.12.1", _system.Calls);
    }

    [Fact]
    public async Task Route_through_another_adapter_does_not_count()
    {
        _system.DefaultRoutes.Clear();
        _system.DefaultRoutes.Add(new DefaultRoute(Guid.NewGuid(), "192.168.0.1"));

        await ApplyAsync();

        Assert.Contains("add route 10.68.12.1", _system.Calls);
    }

    [Fact]
    public async Task Route_that_cannot_be_added_is_a_warning()
    {
        _system.DefaultRoutes.Clear();
        _system.RouteFailure = new InvalidOperationException("Windows не дала добавить маршрут: отказ.");

        IReadOnlyList<StepResult> results = await ApplyAsync();

        StepResult route = Step(results, "Маршрут по умолчанию");
        Assert.Equal(StepOutcome.Warning, route.Outcome);
        Assert.Equal("Маршрута по умолчанию нет. Windows не дала добавить маршрут: отказ.", route.Message);
    }

    [Fact]
    public async Task Route_that_does_not_show_up_after_adding_is_a_warning()
    {
        // Как с netsh: успеху команды не верим, верим прочитанному (ADR 0003)
        _system.DefaultRoutes.Clear();
        _system.RouteIgnored = true;

        IReadOnlyList<StepResult> results = await ApplyAsync();

        StepResult route = Step(results, "Маршрут по умолчанию");
        Assert.Equal(StepOutcome.Warning, route.Outcome);
        Assert.Equal("Маршрута по умолчанию нет: Windows приняла команду, но маршрут через 10.68.12.1 не появился.", route.Message);
    }

    [Fact]
    public async Task Dhcp_route_is_added_through_the_received_gateway()
    {
        _system.DefaultRoutes.Clear();
        _wifi.Active.Gateways[0] = "192.168.1.1";

        await ApplyAsync(Profile.Automatic);

        Assert.Contains("add route 192.168.1.1", _system.Calls);
    }

    [Fact]
    public async Task Dhcp_without_gateway_and_route_is_a_warning()
    {
        _system.DefaultRoutes.Clear();
        _wifi.Active.Addresses.Clear();
        _wifi.Active.Gateways.Clear();

        IReadOnlyList<StepResult> results = await ApplyAsync(Profile.Automatic);

        StepResult route = Step(results, "Маршрут по умолчанию");
        Assert.Equal(StepOutcome.Warning, route.Outcome);
        Assert.Equal("Маршрута по умолчанию нет, и шлюз неизвестен: DHCP не ответил.", route.Message);
    }

    [Fact]
    public async Task Static_profile_without_gateway_is_not_checked_for_route_or_gateway()
    {
        _system.DefaultRoutes.Clear();
        _wifi.Active.Gateways.Clear();
        Profile direct = _office.Clone();
        direct.Ipv4.Gateway = "";

        IReadOnlyList<StepResult> results = await ApplyAsync(direct);

        Assert.Equal(["Настройки IPv4 адаптера «Беспроводная сеть»", "Настройки прокси", "Адрес", "Порт прокси"], results.Select(result => result.Title));
        Assert.DoesNotContain(_system.Calls, call => call.StartsWith("add route") || call.StartsWith("ping"));
    }

    [Fact]
    public async Task Address_received_within_ten_seconds_is_fine()
    {
        _wifi.Active.Addresses.Clear();
        _system.Later(TimeSpan.FromSeconds(8), () => _wifi.Active.Addresses.Add("10.68.12.37/24"));

        IReadOnlyList<StepResult> results = await ApplyAsync();

        Assert.Equal(StepOutcome.Done, Step(results, "Адрес").Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("169.254.12.37/16")]
    public async Task No_address_after_ten_seconds_is_a_warning(string? selfAssigned)
    {
        _wifi.Active.Addresses.Clear();
        if (selfAssigned != null) _wifi.Active.Addresses.Add(selfAssigned);

        IReadOnlyList<StepResult> results = await ApplyAsync(Profile.Automatic);

        StepResult address = Step(results, "Адрес");
        Assert.Equal(StepOutcome.Warning, address.Outcome);
        Assert.Equal("Адрес не получен за 10 с: адреса нет или он 169.254.x.x.", address.Message);
        Assert.InRange(_time.Elapsed, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11));
    }

    [Fact]
    public async Task Static_profile_waits_for_its_own_address_not_the_previous_one()
    {
        _wifi.Active.Addresses[0] = "192.168.1.37/24";

        IReadOnlyList<StepResult> results = await ApplyAsync();

        StepResult address = Step(results, "Адрес");
        Assert.Equal(StepOutcome.Warning, address.Outcome);
        Assert.Equal("Адрес 10.68.12.37 не появился на адаптере за 10 с.", address.Message);
    }

    [Fact]
    public async Task Address_conflict_is_a_warning()
    {
        _wifi.Active.Addresses.Clear();
        _wifi.Active.HasConflict = true;

        IReadOnlyList<StepResult> results = await ApplyAsync();

        StepResult address = Step(results, "Адрес");
        Assert.Equal(StepOutcome.Warning, address.Outcome);
        Assert.Equal("Адрес 10.68.12.37 занят: в сети есть другой компьютер с таким же адресом.", address.Message);
    }

    [Fact]
    public async Task Gateway_gets_three_pings()
    {
        _system.LostPings = 2;

        IReadOnlyList<StepResult> results = await ApplyAsync();

        Assert.Equal(StepOutcome.Done, Step(results, "Шлюз").Outcome);
        Assert.Equal(3, _system.Calls.Count(call => call == "ping 10.68.12.1"));
    }

    [Fact]
    public async Task Silent_gateway_is_a_warning()
    {
        _system.Answering.Clear();

        IReadOnlyList<StepResult> results = await ApplyAsync();

        StepResult gateway = Step(results, "Шлюз");
        Assert.Equal(StepOutcome.Warning, gateway.Outcome);
        Assert.Equal("Шлюз 10.68.12.1 не ответил на ping: возможно, адрес не из подсети этой сети.", gateway.Message);
        Assert.Equal(3, _system.Calls.Count(call => call == "ping 10.68.12.1"));
    }

    [Fact]
    public async Task Closed_proxy_port_is_a_warning()
    {
        _system.OpenPorts.Clear();

        IReadOnlyList<StepResult> results = await ApplyAsync();

        StepResult proxy = Step(results, "Порт прокси");
        Assert.Equal(StepOutcome.Warning, proxy.Outcome);
        Assert.Equal("Прокси 10.0.68.52:3128 не принял соединение за 3 с.", proxy.Message);
    }

    [Fact]
    public async Task Proxy_port_is_not_checked_when_proxy_is_off()
    {
        Profile direct = _office.Clone();
        direct.Proxy.Enabled = false;

        IReadOnlyList<StepResult> results = await ApplyAsync(direct);

        Assert.DoesNotContain(results, result => result.Title == "Порт прокси");
        Assert.DoesNotContain(_system.Calls, call => call.StartsWith("connect"));
    }

    [Fact]
    public async Task Proxy_server_without_port_is_a_warning()
    {
        Profile odd = _office.Clone();
        odd.Proxy.Server = "proxy.corp.local";

        IReadOnlyList<StepResult> results = await ApplyAsync(odd);

        StepResult proxy = Step(results, "Порт прокси");
        Assert.Equal(StepOutcome.Warning, proxy.Outcome);
        Assert.Equal("Порт прокси не проверен: в адресе «proxy.corp.local» нет порта.", proxy.Message);
    }

    [Fact]
    public async Task Failed_ipv4_step_does_not_stop_the_others()
    {
        _system.Ipv4Failure = new InvalidOperationException("netsh: отказ.");

        IReadOnlyList<StepResult> results = await ApplyAsync();

        Assert.Equal(StepOutcome.Error, results[0].Outcome);
        Assert.Equal(6, results.Count);
        Assert.Contains("winhttp", _system.Calls);
    }

    [Fact]
    public async Task Applied_profile_becomes_the_last_application_and_leaves_no_mismatch()
    {
        // Сеть привязана к «Автоматически», а человек применил к ней «Офис»
        var profiles = new ProfileSet();
        Assert.Empty(profiles.Add(_office));
        profiles.Bind("Office", Profile.AutomaticId);
        var wifi = new FakeWifi { Current = Encoding.UTF8.GetBytes("Office") };
        Assert.Null(_applier.Last);

        await ApplyAsync();

        Assert.Equal(new LastApplication("Office", _office.Id), _applier.Last);
        CurrentState state = await CurrentState.ReadAsync(_system, wifi, profiles, _applier.Last, CancellationToken.None);
        Assert.Equal(_office.Id, state.ExpectedProfile?.Id);
        Assert.Null(state.Mismatch);
        Assert.Equal(Profile.AutomaticId, profiles.Bindings["Office"]);
    }
}
