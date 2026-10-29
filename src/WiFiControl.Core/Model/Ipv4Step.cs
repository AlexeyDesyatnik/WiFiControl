namespace WiFiControl.Core.Model;

/// <summary>
/// Шаг применения «настройки IPv4 на Wi-Fi-адаптере». Перед записью запоминает прежние настройки
/// адаптера, свежо прочитанные из системы: по ним вызывающий выполняет откат.
/// </summary>
public sealed class Ipv4Step
{
    private readonly INetworkSystem _system;
    private readonly Adapter _adapter;
    private readonly Ipv4Settings _settings;

    /// <param name="title">Название шага; по умолчанию — «Настройки IPv4 адаптера…».</param>
    public Ipv4Step(INetworkSystem system, Adapter adapter, Ipv4Settings settings, string? title = null)
    {
        _system = system;
        _adapter = adapter;
        _settings = settings.Clone();
        Title = title ?? "Настройки IPv4 адаптера «" + adapter.Name + "»";
    }

    public string Title { get; }

    /// <summary>Настройки адаптера перед записью; null, если до записи дело не дошло.</summary>
    public Ipv4Settings? Previous { get; private set; }

    public Task<StepResult> RunAsync(CancellationToken cancellationToken)
    {
        return StepRunner.RunAsync(Title, WriteAsync, cancellationToken);
    }

    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        // Между чтением и применением адаптер мог измениться: его включили, к нему подключились, настройки поправили
        IReadOnlyList<Adapter> adapters = await _system.ReadAdaptersAsync(cancellationToken).ConfigureAwait(false);
        Adapter fresh = adapters.FirstOrDefault(candidate => string.Equals(candidate.Id, _adapter.Id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Адаптер «" + _adapter.Name + "» больше не найден.");

        Previous = fresh.Configured.Clone();
        await _system.WriteIpv4Async(fresh, _settings, cancellationToken).ConfigureAwait(false);
    }
}
