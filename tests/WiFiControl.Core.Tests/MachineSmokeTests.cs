using WiFiControl.Core.Model;
using WiFiControl.Core.Windows;

namespace WiFiControl.Core.Tests;

/// <summary>
/// Читают настройки компьютера, на котором запущены тесты. Ничего не меняют
/// и прав администратора не требуют.
/// </summary>
[Trait("Category", "Machine")]
public class MachineSmokeTests
{
    [Fact]
    public async Task Reads_adapters_of_this_machine()
    {
        IReadOnlyList<Adapter> adapters = await new WindowsNetworkSystem().ReadAdaptersAsync(CancellationToken.None);

        Assert.NotEmpty(adapters);
        Assert.All(adapters, adapter =>
        {
            Assert.NotEqual("", adapter.Name);
            Assert.StartsWith("{", adapter.Id);
        });
    }

    [Fact]
    public async Task Reads_state_of_one_adapter_as_in_the_full_list()
    {
        Adapter adapter = (await new WindowsNetworkSystem().ReadAdaptersAsync(CancellationToken.None))[0];

        Assert.Equal(adapter.State, AdapterReader.ReadState(adapter.Id));
    }

    [Fact]
    public async Task Reads_proxy_of_this_machine()
    {
        ProxyState state = await new WindowsNetworkSystem().ReadProxyAsync(CancellationToken.None);

        // WinHTTP отдаёт сервер только вместе с признаком «через прокси»
        if (!state.WinHttp.Enabled) Assert.Equal("", state.WinHttp.Server);
    }

    [Fact]
    public async Task Reads_wifi_adapters_and_finds_them_among_network_adapters()
    {
        IReadOnlyList<WifiAdapter> wifi = await new WindowsWifiSystem().ReadAdaptersAsync(CancellationToken.None);
        IReadOnlyList<Adapter> adapters = await new WindowsNetworkSystem().ReadAdaptersAsync(CancellationToken.None);

        Assert.NotEmpty(wifi);
        Assert.All(wifi, adapter => Assert.Contains(adapters, candidate => Guid.Parse(candidate.Id) == adapter.Id));
    }

    [Fact]
    public async Task Reads_windows_profiles_of_the_wifi_adapter()
    {
        var system = new WindowsWifiSystem();
        WifiAdapter adapter = (await system.ReadAdaptersAsync(CancellationToken.None))[0];

        IReadOnlyList<WindowsProfile> profiles = await system.ReadWindowsProfilesAsync(adapter.Id, CancellationToken.None);

        Assert.All(profiles, profile =>
        {
            Assert.NotEqual("", profile.Name);
            Assert.NotEqual("", profile.Network);
        });
    }

    [Fact]
    public async Task Reads_cached_networks_and_current_network_without_scanning()
    {
        var system = new WindowsWifiSystem();
        WifiAdapter adapter = (await system.ReadAdaptersAsync(CancellationToken.None))[0];

        IReadOnlyList<VisibleNetwork> networks = await system.ReadVisibleNetworksAsync(adapter.Id, CancellationToken.None);
        byte[]? current = await system.ReadCurrentNetworkAsync(adapter.Id, CancellationToken.None);

        Assert.All(networks, network => Assert.InRange(network.Signal, 0, 100));
        if (current != null) Assert.Contains(networks, network => network.Ssid.SequenceEqual(current));
    }

    [Fact]
    public async Task Runs_netsh_hidden_and_reads_its_answer()
    {
        // Команда только показывает интерфейсы и ничего не меняет
        Assert.Null(await Netsh.RunAsync("interface ipv4 show interfaces", CancellationToken.None));
        Assert.NotNull(await Netsh.RunAsync("interface ipv4 show nosuchthing", CancellationToken.None));
    }
}
