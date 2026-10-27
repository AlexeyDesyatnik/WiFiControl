using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class Ipv4StepTests
{
    private readonly FakeSystem _system = new();

    public Ipv4StepTests()
    {
        Adapter wifi = FakeSystem.WiFi();
        wifi.Configured = FakeSystem.Static("192.168.1.50", "192.168.1.1", "192.168.1.1");
        _system.Adapters.Add(wifi);
    }

    [Fact]
    public async Task Writes_profile_settings_and_keeps_freshly_read_previous_ones()
    {
        // Адаптер прочитан раньше, и с тех пор его настройки сменились
        Adapter stale = FakeSystem.WiFi();
        var step = new Ipv4Step(_system, stale, FakeSystem.Static("10.68.12.37", "10.68.12.1", "8.8.8.8"));

        StepResult result = await step.RunAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("Настройки IPv4 адаптера «Беспроводная сеть»", result.Title);
        Assert.Equal(["ipv4 Беспроводная сеть"], _system.Calls);
        Assert.Equal("10.68.12.37", _system.Adapters[0].Configured.Address);
        Assert.Equal(["8.8.8.8"], _system.Adapters[0].Configured.Dns);
        Assert.NotNull(step.Previous);
        Assert.True(step.Previous.SameAs(FakeSystem.Static("192.168.1.50", "192.168.1.1", "192.168.1.1")));
    }

    [Fact]
    public async Task Failed_write_gives_the_error_and_still_keeps_previous_settings()
    {
        _system.Ipv4Failure = new InvalidOperationException("Windows приняла команды, но настройки адаптера не совпали с заданными.");
        var step = new Ipv4Step(_system, FakeSystem.WiFi(), new Ipv4Settings());

        StepResult result = await step.RunAsync(CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("Windows приняла команды, но настройки адаптера не совпали с заданными.", result.Error);
        Assert.Equal("192.168.1.50", step.Previous?.Address);
    }

    [Fact]
    public async Task Nothing_is_written_when_the_adapter_is_gone()
    {
        _system.Adapters.Clear();
        var step = new Ipv4Step(_system, FakeSystem.WiFi(), new Ipv4Settings());

        StepResult result = await step.RunAsync(CancellationToken.None);

        Assert.Equal("Адаптер «Беспроводная сеть» больше не найден.", result.Error);
        Assert.Empty(_system.Calls);
        Assert.Null(step.Previous);
    }

    [Fact]
    public async Task Nothing_is_written_when_adapters_cannot_be_read()
    {
        _system.AdaptersFailure = new InvalidOperationException("Windows не отдала список сетевых адаптеров: отказ.");
        var step = new Ipv4Step(_system, FakeSystem.WiFi(), new Ipv4Settings());

        StepResult result = await step.RunAsync(CancellationToken.None);

        Assert.Equal("Windows не отдала список сетевых адаптеров: отказ.", result.Error);
        Assert.Empty(_system.Calls);
        Assert.Null(step.Previous);
    }

    [Fact]
    public async Task Cancelled_step_is_not_a_failed_step()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var step = new Ipv4Step(_system, FakeSystem.WiFi(), new Ipv4Settings());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => step.RunAsync(cancelled.Token));
        Assert.Empty(_system.Calls);
    }
}
