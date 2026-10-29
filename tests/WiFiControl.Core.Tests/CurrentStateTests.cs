using System.Text;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class CurrentStateTests
{
    private readonly FakeSystem _system = new();
    private readonly FakeWifi _wifi = new();
    private readonly ProfileSet _profiles = new();

    public CurrentStateTests()
    {
        _system.Adapters.Add(FakeSystem.WiFi(AdapterState.Connected));
    }

    private Task<CurrentState> ReadAsync(ProfileSet? profiles = null)
    {
        return CurrentState.ReadAsync(_system, _wifi, profiles ?? _profiles, CancellationToken.None);
    }

    [Fact]
    public async Task Network_seen_by_several_access_points_is_one_row_with_the_strongest_signal()
    {
        _wifi.Networks.Add(FakeWifi.Entry("Office", 40));
        _wifi.Networks.Add(FakeWifi.Entry("Office", 85));
        _wifi.Networks.Add(FakeWifi.Entry("Office", 60));

        CurrentState state = await ReadAsync();

        NetworkRow row = Assert.Single(state.Networks);
        Assert.Equal("Office", row.Network);
        Assert.Equal(85, row.Signal);
    }

    [Fact]
    public async Task Security_and_windows_profile_come_from_the_strongest_entry()
    {
        _wifi.Networks.Add(FakeWifi.Entry("Office", 40, WifiSecurity.Wpa2Personal, "Office-old"));
        _wifi.Networks.Add(FakeWifi.Entry("Office", 85, WifiSecurity.Wpa3Personal, "Office"));

        CurrentState state = await ReadAsync();

        NetworkRow row = Assert.Single(state.Networks);
        Assert.Equal(WifiSecurity.Wpa3Personal, row.Security);
        Assert.Equal("Office", row.WindowsProfileName);
    }

    [Fact]
    public async Task Of_equally_strong_entries_the_one_with_a_windows_profile_wins()
    {
        // Windows отдаёт запись на каждый профиль сети Windows и ещё одну без профиля, с тем же сигналом
        _wifi.Networks.Add(FakeWifi.Entry("Office", 70));
        _wifi.Networks.Add(FakeWifi.Entry("Office", 70, windowsProfile: "Office"));
        _wifi.Networks.Add(FakeWifi.Entry("Office", 70));

        CurrentState state = await ReadAsync();

        Assert.Equal("Office", Assert.Single(state.Networks).WindowsProfileName);
    }

    [Fact]
    public async Task Entries_with_empty_name_are_not_shown()
    {
        _wifi.Networks.Add(FakeWifi.Entry("", 90));
        _wifi.Networks.Add(FakeWifi.Entry("Office", 50));

        CurrentState state = await ReadAsync();

        Assert.Equal("Office", Assert.Single(state.Networks).Network);
    }

    [Fact]
    public async Task Name_that_is_not_utf8_is_a_hex_key()
    {
        // «Привет» в cp1251
        _wifi.Networks.Add(new VisibleNetwork([0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2], 50, WifiSecurity.Wpa2Personal, ""));

        CurrentState state = await ReadAsync();

        Assert.Equal("hex:CFF0E8E2E5F2", Assert.Single(state.Networks).Network);
    }

    [Fact]
    public async Task Current_network_goes_first_and_the_rest_by_signal_descending()
    {
        _wifi.Networks.Add(FakeWifi.Entry("Cafe", 60));
        _wifi.Networks.Add(FakeWifi.Entry("Home", 30));
        _wifi.Networks.Add(FakeWifi.Entry("Neighbour", 90));
        _wifi.Current = Encoding.UTF8.GetBytes("Home");

        CurrentState state = await ReadAsync();

        Assert.Equal(["Home", "Neighbour", "Cafe"], state.Networks.Select(row => row.Network));
        Assert.Equal([true, false, false], state.Networks.Select(row => row.IsCurrent));
        Assert.Equal("Home", state.Network);
    }

    [Fact]
    public async Task Each_row_shows_its_profile_bound_or_default()
    {
        var office = new Profile { Name = "Офис", Ipv4 = FakeSystem.Static("10.68.12.37") };
        var lab = new Profile { Name = "Лаборатория", Ipv4 = FakeSystem.Static("10.68.13.37", "10.68.13.1") };
        _profiles.Add(office);
        _profiles.Add(lab);
        _profiles.Bind("Office", office.Id);
        // Явная привязка к профилю по умолчанию — всё равно привязка
        _profiles.Bind("Lab", lab.Id);
        _profiles.DefaultProfileId = lab.Id;
        _wifi.Networks.Add(FakeWifi.Entry("Office", 90));
        _wifi.Networks.Add(FakeWifi.Entry("Lab", 80));
        _wifi.Networks.Add(FakeWifi.Entry("Cafe", 70));

        CurrentState state = await ReadAsync();

        Assert.Equal(["Офис", "Лаборатория", "Лаборатория"], state.Networks.Select(row => row.Profile?.Name));
        Assert.Equal([true, true, false], state.Networks.Select(row => row.IsBound));
    }

    [Fact]
    public async Task Without_profile_set_the_profile_of_rows_is_unknown()
    {
        _wifi.Networks.Add(FakeWifi.Entry("Office", 90));

        CurrentState state = await CurrentState.ReadAsync(_system, _wifi, profiles: null, CancellationToken.None);

        NetworkRow row = Assert.Single(state.Networks);
        Assert.Null(row.Profile);
        Assert.False(row.IsBound);
    }

    [Fact]
    public async Task Now_block_has_addresses_of_the_wifi_adapter_and_the_proxy()
    {
        var wired = new Adapter { Id = "{00000000-0000-0000-0000-000000000003}", Name = "Ethernet", State = AdapterState.Connected };
        wired.Active.Addresses.Add("192.168.1.10/24");
        _system.Adapters.Insert(0, wired);
        Adapter wifi = _system.Adapters[1];
        wifi.Configured = FakeSystem.Static("10.68.12.37", "10.68.12.1", "10.68.0.2");
        wifi.Active.Addresses.Add("10.68.12.37/24");
        wifi.Active.Gateways.Add("10.68.12.1");
        wifi.Active.Dns.Add("10.68.0.2");
        _system.SystemProxy = new SystemProxyState { Settings = ProxySettings.FromRaw(true, "proxy:3128", "<local>") };

        CurrentState state = await ReadAsync();

        Assert.Equal(FakeWifi.WifiId, state.WifiAdapter?.Id);
        Assert.Equal("Беспроводная сеть", state.Adapter?.Name);
        Assert.Equal(["10.68.12.37/24"], state.Adapter?.Active.Addresses);
        Assert.Equal(["10.68.12.1"], state.Adapter?.Active.Gateways);
        Assert.Equal(["10.68.0.2"], state.Adapter?.Active.Dns);
        Assert.False(state.Adapter?.Configured.AutoAddress);
        Assert.Equal("proxy:3128", state.Proxy.SystemProxy.Settings.Server);
        Assert.True(state.Proxy.SystemProxy.Settings.BypassLocal);
    }

    [Fact]
    public async Task Now_block_shows_the_profile_of_the_current_network()
    {
        var office = new Profile { Name = "Офис" };
        _profiles.Add(office);
        _profiles.Bind("Office", office.Id);
        _wifi.Current = Encoding.UTF8.GetBytes("Office");

        CurrentState state = await ReadAsync();

        // Текущей сети может не быть в списке видимых: профиль всё равно известен
        Assert.Empty(state.Networks);
        Assert.Equal("Офис", state.Profile?.Name);
        Assert.True(state.IsBound);
    }

    [Fact]
    public async Task Disconnected_adapter_has_no_network_and_no_profile()
    {
        CurrentState state = await ReadAsync();

        Assert.Null(state.Network);
        Assert.Null(state.Profile);
        Assert.False(state.IsBound);
    }

    [Fact]
    public async Task Without_wifi_adapters_the_state_is_empty_and_nothing_is_read_from_wifi()
    {
        _wifi.Adapters.Clear();
        _wifi.Networks.Add(FakeWifi.Entry("Office", 90));

        CurrentState state = await ReadAsync();

        Assert.Null(state.WifiAdapter);
        Assert.Null(state.Adapter);
        Assert.Null(state.Network);
        Assert.Empty(state.Networks);
    }
}
