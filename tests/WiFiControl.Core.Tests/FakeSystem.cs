using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

/// <summary>Компьютер в памяти: отдаёт заданные тестом адаптеры или заданный отказ. Перенесено из NetSwitch.</summary>
internal sealed class FakeSystem : INetworkSystem
{
    public List<Adapter> Adapters { get; } = [];

    public Exception? AdaptersFailure { get; set; }

    public async Task<IReadOnlyList<Adapter>> ReadAdaptersAsync(CancellationToken cancellationToken)
    {
        // Как настоящая система, отвечает не сразу: вызывающий не должен полагаться на синхронный ответ
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (AdaptersFailure != null) throw AdaptersFailure;
        return [.. Adapters];
    }
}
