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
    public async Task Runs_netsh_hidden_and_reads_its_answer()
    {
        // Команда только показывает интерфейсы и ничего не меняет
        Assert.Null(await Netsh.RunAsync("interface ipv4 show interfaces", CancellationToken.None));
        Assert.NotNull(await Netsh.RunAsync("interface ipv4 show nosuchthing", CancellationToken.None));
    }
}
