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
}
