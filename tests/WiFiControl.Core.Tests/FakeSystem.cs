using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

/// <summary>
/// Компьютер в памяти: отдаёт заданные тестом адаптеры или заданный отказ, а запись меняет то,
/// что вернёт следующее чтение, как это происходит с настоящей системой. Перенесено из NetSwitch.
/// </summary>
internal sealed class FakeSystem : INetworkSystem
{
    public List<Adapter> Adapters { get; } = [];

    /// <summary>Выполненные записи по порядку.</summary>
    public List<string> Calls { get; } = [];

    public Exception? AdaptersFailure { get; set; }

    public Exception? Ipv4Failure { get; set; }

    public static Adapter WiFi(AdapterState state = AdapterState.NoLink)
    {
        return new Adapter
        {
            Id = "{00000000-0000-0000-0000-000000000002}",
            Name = "Беспроводная сеть",
            Description = "Intel(R) Wi-Fi 6E AX211 160MHz",
            IsPhysical = true,
            IsWireless = true,
            State = state
        };
    }

    public static Ipv4Settings Static(string address, string gateway = "10.68.12.1", params string[] dns)
    {
        var settings = new Ipv4Settings { AutoAddress = false, Address = address, Mask = "255.255.255.0", Gateway = gateway, AutoDns = false };
        settings.Dns.AddRange(dns);
        return settings;
    }

    public async Task<IReadOnlyList<Adapter>> ReadAdaptersAsync(CancellationToken cancellationToken)
    {
        // Как настоящая система, отвечает не сразу: вызывающий не должен полагаться на синхронный ответ
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (AdaptersFailure != null) throw AdaptersFailure;
        return [.. Adapters];
    }

    public async Task WriteIpv4Async(Adapter adapter, Ipv4Settings settings, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        Calls.Add("ipv4 " + adapter.Name);
        if (Ipv4Failure != null) throw Ipv4Failure;
        Adapters.Single(candidate => candidate.Id == adapter.Id).Configured = settings.Clone();
    }
}
