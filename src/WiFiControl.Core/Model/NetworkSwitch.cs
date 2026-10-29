using System.Globalization;

namespace WiFiControl.Core.Model;

/// <summary>Что переключение делает с профилем сети Windows перед подключением.</summary>
internal abstract record WindowsProfileChange;

/// <summary>Новая сеть: профиль сети Windows создаётся, а если подключиться не удалось — удаляется.</summary>
internal sealed record CreateWindowsProfile(NewWindowsProfile Profile) : WindowsProfileChange;

/// <summary>«Ввести пароль заново»: пароль в существующем профиле сети Windows перезаписывается и при неудаче не возвращается.</summary>
internal sealed record ReplacePassword(string Password) : WindowsProfileChange
{
    // Пароль не должен попасть в текст
    public override string ToString() => nameof(ReplacePassword);
}

/// <summary>
/// Переключение по ADR 0004: отключиться от текущей сети, настройки IPv4, подключиться, настройки прокси, проверка.
/// Неудача только одна — подключиться не удалось; тогда откат: прежние настройки IPv4, удаление профиля сети Windows,
/// если его создало это же переключение, и возврат в прежнюю сеть.
/// </summary>
internal sealed class NetworkSwitch(INetworkSystem system, IWifiSystem wifi, TimeProvider time, Adapter adapter, Action<StepResult> report)
{
    // ADR 0004: удача приходит за 0,1–3 с, неверный пароль — неудача через ~26 с; о неудаче Windows сообщает сама,
    // срок — только страховка
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(45);

    private readonly Guid _adapterId = Guid.Parse(adapter.Id);

    // Код причины последней неудачи подключения; 0 — Windows о неудаче не сообщала
    private int _failureReason;

    /// <param name="windowsProfile">Профиль сети Windows, через который подключаться.</param>
    /// <param name="change">Что сделать с этим профилем перед подключением; null — ничего.</param>
    /// <returns>Состоялось ли подключение и, если нет, отвергла ли Windows пароль.</returns>
    public async Task<(bool Connected, bool PasswordRejected)> RunAsync(string network, string windowsProfile, WindowsProfileChange? change,
        Profile profile, CancellationToken cancellationToken)
    {
        string? previous;
        try
        {
            byte[]? current = await wifi.ReadCurrentNetworkAsync(_adapterId, cancellationToken).ConfigureAwait(false);
            previous = current == null ? null : NetworkKey.FromBytes(current);
        }
        // Ничего ещё не изменено, а без прежней сети нет и отката: не начинаем
        catch (InvalidOperationException e)
        {
            report(new StepResult("Текущая сеть", "Переключение не начато. " + e.Message));
            return (false, false);
        }

        if (previous != null)
        {
            report(await StepRunner.RunAsync("Отключение от сети «" + previous + "»", token => wifi.DisconnectAsync(_adapterId, token), cancellationToken)
                .ConfigureAwait(false));
        }
        var ipv4 = new Ipv4Step(system, adapter, profile.Ipv4);
        report(await ipv4.RunAsync(cancellationToken).ConfigureAwait(false));

        if (change != null)
        {
            StepResult changed = await ChangeAsync(change, windowsProfile, cancellationToken).ConfigureAwait(false);
            report(changed);
            if (!changed.Ok)
            {
                // Профиль не создан или пароль не записан: подключаться не через что, удалять нечего
                await RollBackAsync(ipv4.Previous, null, previous, cancellationToken).ConfigureAwait(false);
                return (false, false);
            }
        }

        StepResult connect = await StepRunner.RunAsync("Подключение к сети «" + network + "»", token => ConnectAsync(network, windowsProfile, token),
            cancellationToken).ConfigureAwait(false);
        report(connect);
        if (!connect.Ok)
        {
            // Причину запоминаем до отката: возврат в прежнюю сеть тоже подключается
            bool rejected = _failureReason == WlanReason.PskMismatchSuspected;
            await RollBackAsync(ipv4.Previous, change is CreateWindowsProfile ? windowsProfile : null, previous, cancellationToken).ConfigureAwait(false);
            return (false, rejected);
        }

        // Ошибки шагов дальше — не повод для отката: связь с новой сетью есть
        report(await new ProxyStep(system, profile.Proxy).RunAsync(cancellationToken).ConfigureAwait(false));
        await new ConnectionCheck(system, time, adapter, profile).RunAsync(report, cancellationToken).ConfigureAwait(false);
        return (true, false);
    }

    private Task<StepResult> ChangeAsync(WindowsProfileChange change, string windowsProfile, CancellationToken cancellationToken) => change switch
    {
        // Без перезаписи: существующий профиль сети Windows с тем же именем переключение не трогает
        CreateWindowsProfile create => StepRunner.RunAsync("Создание профиля сети Windows «" + windowsProfile + "»",
            token => wifi.SaveWindowsProfileAsync(_adapterId, create.Profile.Xml, overwrite: false, token), cancellationToken),
        ReplacePassword replace => StepRunner.RunAsync("Новый пароль в профиле сети Windows «" + windowsProfile + "»",
            token => ReplacePasswordAsync(windowsProfile, replace.Password, token), cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
    };

    private async Task ReplacePasswordAsync(string windowsProfile, string password, CancellationToken cancellationToken)
    {
        string xml = await wifi.ReadWindowsProfileXmlAsync(_adapterId, windowsProfile, cancellationToken).ConfigureAwait(false);
        await wifi.SaveWindowsProfileAsync(_adapterId, WindowsProfileXml.WithPassword(xml, password), overwrite: true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Прокси при откате не трогается: до его записи дело не дошло.</summary>
    /// <param name="ipv4">Настройки IPv4 до переключения; null, если их не успели прочитать — тогда и не записали.</param>
    /// <param name="created">Профиль сети Windows, созданный этим переключением; null, если не создавали.</param>
    private async Task RollBackAsync(Ipv4Settings? ipv4, string? created, string? previous, CancellationToken cancellationToken)
    {
        if (ipv4 != null) report(await new Ipv4Step(system, adapter, ipv4, "Откат настроек IPv4").RunAsync(cancellationToken).ConfigureAwait(false));
        if (created != null)
        {
            // Чтобы Windows не запомнила неверный пароль
            report(await StepRunner.RunAsync("Удаление профиля сети Windows «" + created + "»",
                token => wifi.DeleteWindowsProfileAsync(_adapterId, created, token), cancellationToken).ConfigureAwait(false));
        }
        if (previous != null)
        {
            report(await StepRunner.RunAsync("Возврат в сеть «" + previous + "»", token => ReturnAsync(previous, token), cancellationToken)
                .ConfigureAwait(false));
        }
    }

    private async Task ReturnAsync(string previous, CancellationToken cancellationToken)
    {
        // Пока адаптер отключён, Windows сама никуда не подключалась, но это проверено не во всех случаях (ADR 0004)
        byte[]? current = await wifi.ReadCurrentNetworkAsync(_adapterId, cancellationToken).ConfigureAwait(false);
        if (current != null && NetworkKey.Comparer.Equals(NetworkKey.FromBytes(current), previous)) return;

        IReadOnlyList<WindowsProfile> profiles = await wifi.ReadWindowsProfilesAsync(_adapterId, cancellationToken).ConfigureAwait(false);
        WindowsProfile profile = profiles.FirstOrDefault(candidate => NetworkKey.Comparer.Equals(candidate.Network, previous))
            ?? throw new InvalidOperationException("У сети «" + previous + "» больше нет профиля сети Windows.");
        await ConnectAsync(previous, profile.Name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Подключается и ждёт исхода по уведомлениям; неудача — <see cref="InvalidOperationException"/> с причиной.</summary>
    private async Task ConnectAsync(string network, string windowsProfile, CancellationToken cancellationToken)
    {
        _failureReason = 0;
        // Код причины неудачи; 0 — подключено
        var outcome = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Настоящая причина приходит в попытке, а итог — уже с общим кодом «сеть недоступна» (docs/connect-check.md).
        // Уведомления одного клиента приходят по очереди, поэтому без блокировки
        int attemptReason = 0;
        void OnNotification(WifiNotification notification)
        {
            switch (notification)
            {
                case WifiConnected connected when Ours(connected, connected.Network):
                    outcome.TrySetResult(0);
                    break;
                case WifiConnectionAttemptFailed attempt when Ours(attempt, attempt.Network):
                    attemptReason = attempt.Reason;
                    break;
                case WifiConnectionFailed failed when Ours(failed, failed.Network):
                    outcome.TrySetResult(attemptReason != 0 ? attemptReason : failed.Reason);
                    break;
            }
        }
        bool Ours(WifiNotification notification, string key) => notification.Adapter == _adapterId && NetworkKey.Comparer.Equals(key, network);

        int reason;
        // Подписка — до команды: удача приходит через 0,1 с
        using (wifi.Watch(OnNotification))
        {
            await wifi.ConnectAsync(_adapterId, windowsProfile, cancellationToken).ConfigureAwait(false);
            try
            {
                reason = await outcome.Task.WaitAsync(ConnectTimeout, time, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                await AbortAsync(cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("Windows не подключилась за " + ConnectTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " с.");
            }
        }
        _failureReason = reason;
        if (reason != 0) throw new InvalidOperationException(WlanReason.Describe(reason));
    }

    /// <summary>Срок вышел, а Windows, может быть, ещё пытается: попытку прерываем, чтобы она не закончилась после отката.</summary>
    private async Task AbortAsync(CancellationToken cancellationToken)
    {
        try
        {
            await wifi.DisconnectAsync(_adapterId, cancellationToken).ConfigureAwait(false);
        }
        // Отключать, скорее всего, нечего
        catch (InvalidOperationException)
        {
        }
    }
}
