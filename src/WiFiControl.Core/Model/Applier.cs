namespace WiFiControl.Core.Model;

/// <summary>
/// Применение профиля к Wi-Fi-адаптеру по шагам: настройки IPv4, настройки прокси, проверка; и переключение,
/// в которое оно входит. Помнит последнее применение до выхода из инструмента.
/// </summary>
public sealed class Applier(INetworkSystem system, IWifiSystem wifi, TimeProvider time)
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

    /// <summary>
    /// Переключение на сеть через её профиль сети Windows (ADR 0004). Если подключиться не удалось — откат.
    /// Переключение, в котором подключение состоялось, становится последним применением, даже с ошибками шагов.
    /// </summary>
    /// <param name="network">Ключ сети, на которую переключаться.</param>
    /// <param name="windowsProfile">Профиль сети Windows этой сети.</param>
    /// <param name="onStep">Вызывается после каждого шага, по порядку.</param>
    public Task<SwitchResult> SwitchAsync(Adapter adapter, string network, string windowsProfile, Profile profile,
        Action<StepResult>? onStep, CancellationToken cancellationToken)
    {
        return RunSwitchAsync(adapter, network, windowsProfile, null, profile, onStep, cancellationToken);
    }

    /// <summary>
    /// Переключение на новую сеть: перед подключением создаётся профиль сети Windows. Если подключиться не удалось,
    /// созданный профиль удаляется при откате; существовавший профиль с тем же именем не перезаписывается.
    /// </summary>
    /// <param name="windowsProfile">Профиль сети Windows из <see cref="WindowsProfileXml.Build"/>.</param>
    public Task<SwitchResult> SwitchToNewAsync(Adapter adapter, string network, NewWindowsProfile windowsProfile, Profile profile,
        Action<StepResult>? onStep, CancellationToken cancellationToken)
    {
        return RunSwitchAsync(adapter, network, windowsProfile.Name, new CreateWindowsProfile(windowsProfile), profile, onStep, cancellationToken);
    }

    /// <summary>
    /// «Ввести пароль заново»: пароль в существующем профиле сети Windows перезаписывается, затем переключение.
    /// Если подключиться снова не удалось, новый пароль остаётся: прежний Windows уже отверг.
    /// </summary>
    public Task<SwitchResult> SwitchWithPasswordAsync(Adapter adapter, string network, string windowsProfile, string password, Profile profile,
        Action<StepResult>? onStep, CancellationToken cancellationToken)
    {
        return RunSwitchAsync(adapter, network, windowsProfile, new ReplacePassword(password), profile, onStep, cancellationToken);
    }

    private async Task<SwitchResult> RunSwitchAsync(Adapter adapter, string network, string windowsProfile, WindowsProfileChange? change,
        Profile profile, Action<StepResult>? onStep, CancellationToken cancellationToken)
    {
        var results = new List<StepResult>();
        void Report(StepResult result)
        {
            results.Add(result);
            onStep?.Invoke(result);
        }

        var networkSwitch = new NetworkSwitch(system, wifi, time, adapter, Report);
        (bool connected, bool passwordRejected) = await networkSwitch.RunAsync(network, windowsProfile, change, profile, cancellationToken)
            .ConfigureAwait(false);
        if (connected) Last = new LastApplication(network, profile.Id);
        return new SwitchResult(results, passwordRejected);
    }
}

/// <summary>Чем закончилось переключение.</summary>
/// <param name="Steps">Результаты шагов по порядку, вместе с откатом.</param>
/// <param name="PasswordRejected">Подключиться не удалось, потому что Windows отвергла пароль: можно «Ввести пароль заново».</param>
public sealed record SwitchResult(IReadOnlyList<StepResult> Steps, bool PasswordRejected);
