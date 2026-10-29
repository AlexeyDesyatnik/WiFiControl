namespace WiFiControl.Core.Model;

/// <summary>
/// Применение профиля к Wi-Fi-адаптеру по шагам: настройки IPv4, настройки прокси, проверка.
/// Помнит последнее применение до выхода из инструмента.
/// </summary>
public sealed class Applier(INetworkSystem system, TimeProvider time)
{
    /// <summary>Последнее применение; null, если применений ещё не было.</summary>
    public LastApplication? Last { get; private set; }

    /// <summary>
    /// «Применить профиль…»: применение к сети, к которой адаптер уже подключён, без отключения и подключения.
    /// Привязку не меняет. Шаги выполняются все, даже после ошибки предыдущего.
    /// </summary>
    /// <param name="network">Ключ текущей сети.</param>
    /// <param name="onStep">Вызывается после каждого шага, по порядку.</param>
    /// <returns>Результаты шагов по порядку.</returns>
    public async Task<IReadOnlyList<StepResult>> ApplyProfileAsync(Adapter adapter, string network, Profile profile, Action<StepResult>? onStep,
        CancellationToken cancellationToken)
    {
        var results = new List<StepResult>();
        void Report(StepResult result)
        {
            results.Add(result);
            onStep?.Invoke(result);
        }

        Report(await new Ipv4Step(system, adapter, profile.Ipv4).RunAsync(cancellationToken).ConfigureAwait(false));
        Report(await new ProxyStep(system, profile.Proxy).RunAsync(cancellationToken).ConfigureAwait(false));
        await new ConnectionCheck(system, time, adapter, profile).RunAsync(Report, cancellationToken).ConfigureAwait(false);

        // Даже после ошибки шага: ожидается применённый профиль, а неудача видна расхождением
        Last = new LastApplication(network, profile.Id);
        return results;
    }
}
