using System.Text;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

/// <summary>Признаки проблем и расхождение в снимке текущего состояния.</summary>
public class CurrentStateProblemsTests
{
    private readonly FakeSystem _system = new();
    private readonly FakeWifi _wifi = new();
    private readonly ProfileSet _profiles = new();
    private readonly Adapter _adapter = FakeSystem.WiFi(AdapterState.Connected);

    /// <summary>Подключён к Office по DHCP с рабочим адресом и маршрутом по умолчанию через Wi-Fi: ни одного признака.</summary>
    public CurrentStateProblemsTests()
    {
        _system.Adapters.Add(_adapter);
        _adapter.Active.Addresses.Add("192.168.1.37/24");
        _adapter.Active.Gateways.Add("192.168.1.1");
        _system.DefaultRoutes.Add(new DefaultRoute(FakeWifi.WifiId, "192.168.1.1"));
        _system.InternetAdapter = FakeWifi.WifiId;
        _wifi.Current = Encoding.UTF8.GetBytes("Office");
    }

    private Task<CurrentState> ReadAsync(LastApplication? last = null)
    {
        return CurrentState.ReadAsync(_system, _wifi, _profiles, last, CancellationToken.None);
    }

    [Fact]
    public async Task Working_connection_has_no_problems()
    {
        CurrentState state = await ReadAsync();

        Assert.Empty(state.Problems);
        Assert.Null(state.Mismatch);
    }

    [Theory]
    [InlineData("169.254.12.37/16")]
    [InlineData(null)]
    public async Task Self_assigned_or_missing_address_is_no_address(string? address)
    {
        _adapter.Active.Addresses.Clear();
        if (address != null) _adapter.Active.Addresses.Add(address);
        _system.DefaultRoutes.Clear();

        CurrentState state = await ReadAsync();

        // Без адреса нет и маршрута: признак один, по причине
        Assert.Equal([Problem.NoAddress], state.Problems);
    }

    [Fact]
    public async Task Address_conflict_is_shown_instead_of_no_address()
    {
        // Адрес-дубликат в действующие не попадает
        _adapter.Active.Addresses.Clear();
        _adapter.Active.HasConflict = true;
        _system.DefaultRoutes.Clear();

        CurrentState state = await ReadAsync();

        Assert.Equal([Problem.AddressConflict], state.Problems);
    }

    [Fact]
    public async Task Working_address_without_default_route_through_wifi_is_no_default_route()
    {
        // Маршрут по умолчанию на отключённом проводном адаптере не в счёт (ADR 0006)
        _system.Adapters.Add(Ethernet(AdapterState.NoLink));
        _system.DefaultRoutes.Clear();
        _system.DefaultRoutes.Add(new DefaultRoute(EthernetId, "192.168.0.1"));
        _system.InternetAdapter = null;

        CurrentState state = await ReadAsync();

        Assert.Equal([Problem.NoDefaultRoute], state.Problems);
    }

    [Fact]
    public async Task Internet_through_wired_adapter_is_a_problem_with_its_name()
    {
        Adapter ethernet = Ethernet(AdapterState.Connected);
        _system.Adapters.Add(ethernet);
        _system.DefaultRoutes.Add(new DefaultRoute(EthernetId, "192.168.0.1"));
        _system.InternetAdapter = EthernetId;

        CurrentState state = await ReadAsync();

        Assert.Equal([Problem.InternetViaEthernet], state.Problems);
        Assert.Equal("Ethernet", state.InternetAdapter?.Name);
    }

    [Fact]
    public async Task Internet_through_wired_adapter_is_shown_without_wifi_connection_too()
    {
        _wifi.Current = null;
        _adapter.State = AdapterState.NoLink;
        _system.Adapters.Add(Ethernet(AdapterState.Connected));
        _system.DefaultRoutes.Clear();
        _system.DefaultRoutes.Add(new DefaultRoute(EthernetId, "192.168.0.1"));
        _system.InternetAdapter = EthernetId;

        CurrentState state = await ReadAsync();

        Assert.Equal([Problem.InternetViaEthernet], state.Problems);
    }

    [Fact]
    public async Task Internet_through_vpn_is_not_through_ethernet()
    {
        var vpn = new Adapter { Id = "{00000000-0000-0000-0000-000000000004}", Name = "VPN", PnpDeviceId = @"ROOT\NET\0000", State = AdapterState.Connected };
        _system.Adapters.Add(vpn);
        _system.InternetAdapter = new Guid(vpn.Id);

        CurrentState state = await ReadAsync();

        Assert.Empty(state.Problems);
        Assert.Equal("VPN", state.InternetAdapter?.Name);
    }

    [Fact]
    public async Task Settings_changed_by_hand_are_a_mismatch_with_the_profile_of_the_network()
    {
        _adapter.Configured = FakeSystem.Static("192.168.1.37", "192.168.1.1", "192.168.1.1");
        _system.SystemProxy = new SystemProxyState { Settings = ProxySettings.FromRaw(true, "10.0.68.52:3128", "") };

        CurrentState state = await ReadAsync();

        Assert.Equal(Profile.AutomaticId, state.ExpectedProfile?.Id);
        Assert.Equal(Profile.AutomaticId, state.Mismatch?.Expected.Id);
        Assert.Equal([Difference.Address, Difference.Dns, Difference.SystemProxy], state.Mismatch?.Differences);
    }

    [Fact]
    public async Task Last_application_in_the_same_network_is_the_expected_profile()
    {
        var home = new Profile { Name = "Дом", Ipv4 = FakeSystem.Static("192.168.1.37", "192.168.1.1", "192.168.1.1") };
        _adapter.Configured = home.Ipv4.Clone();

        CurrentState state = await ReadAsync(new LastApplication("Office", home));

        Assert.Equal(home.Id, state.ExpectedProfile?.Id);
        Assert.Null(state.Mismatch);
    }

    [Fact]
    public async Task Without_profiles_there_is_nothing_to_compare_with()
    {
        _adapter.Configured = FakeSystem.Static("192.168.1.37", "192.168.1.1", "192.168.1.1");

        CurrentState state = await CurrentState.ReadAsync(_system, _wifi, profiles: null, last: null, CancellationToken.None);

        Assert.Null(state.ExpectedProfile);
        Assert.Null(state.Mismatch);
    }

    [Fact]
    public async Task Disconnected_adapter_has_no_mismatch()
    {
        _wifi.Current = null;
        _adapter.State = AdapterState.NoLink;
        _adapter.Configured = FakeSystem.Static("192.168.1.37", "192.168.1.1", "192.168.1.1");

        CurrentState state = await ReadAsync();

        Assert.Null(state.ExpectedProfile);
        Assert.Null(state.Mismatch);
    }

    private static readonly Guid EthernetId = new("00000000-0000-0000-0000-000000000003");

    private static Adapter Ethernet(AdapterState state)
    {
        return new Adapter
        {
            Id = EthernetId.ToString("B"), Name = "Ethernet", Description = "Realtek USB GbE", PnpDeviceId = @"USB\VID_0BDA&PID_8153\000001",
            IsPhysical = true, State = state
        };
    }

    [Fact]
    public async Task Disconnected_adapter_has_no_address_problems()
    {
        _wifi.Current = null;
        _adapter.State = AdapterState.NoLink;
        _adapter.Active.Addresses.Clear();
        _system.DefaultRoutes.Clear();

        CurrentState state = await ReadAsync();

        Assert.Empty(state.Problems);
    }
}
