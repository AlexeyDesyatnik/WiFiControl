using System.Globalization;

namespace WiFiControl.Core.Model;

/// <summary>
/// Переключение по ADR 0004: отключиться от текущей сети, настройки IPv4, подключиться, настройки прокси, проверка.
/// Неудача только одна — подключиться не удалось; тогда откат: прежние настройки IPv4 и возврат в прежнюю сеть.
/// </summary>
internal sealed class NetworkSwitch(INetworkSystem system, IWifiSystem wifi, TimeProvider time, Adapter adapter, Action<StepResult> report)
{
    // ADR 0004: удача приходит за 0,1–3 с, неверный пароль — неудача через ~26 с; о неудаче Windows сообщает сама,
    // срок — только страховка
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(45);

    private readonly Guid _adapterId = Guid.Parse(adapter.Id);

    /// <param name="windowsProfile">Профиль сети Windows, через который подключаться.</param>
    /// <returns>true, если подключение к сети состоялось.</returns>
    public async Task<bool> RunAsync(string network, string windowsProfile, Profile profile, CancellationToken cancellationToken)
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
            return false;
        }

        if (previous != null)
        {
            report(await StepRunner.RunAsync("Отключение от сети «" + previous + "»", token => wifi.DisconnectAsync(_adapterId, token), cancellationToken)
                .ConfigureAwait(false));
        }
        var ipv4 = new Ipv4Step(system, adapter, profile.Ipv4);
        report(await ipv4.RunAsync(cancellationToken).ConfigureAwait(false));

        StepResult connect = await StepRunner.RunAsync("Подключение к сети «" + network + "»", token => ConnectAsync(network, windowsProfile, token),
            cancellationToken).ConfigureAwait(false);
        report(connect);
        if (!connect.Ok)
        {
            await RollBackAsync(ipv4.Previous, previous, cancellationToken).ConfigureAwait(false);
            return false;
        }

        // Ошибки шагов дальше — не повод для отката: связь с новой сетью есть
        report(await new ProxyStep(system, profile.Proxy).RunAsync(cancellationToken).ConfigureAwait(false));
        await new ConnectionCheck(system, time, adapter, profile).RunAsync(report, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Прокси при откате не трогается: до его записи дело не дошло.</summary>
    /// <param name="ipv4">Настройки IPv4 до переключения; null, если их не успели прочитать — тогда и не записали.</param>
    private async Task RollBackAsync(Ipv4Settings? ipv4, string? previous, CancellationToken cancellationToken)
    {
        if (ipv4 != null) report(await new Ipv4Step(system, adapter, ipv4, "Откат настроек IPv4").RunAsync(cancellationToken).ConfigureAwait(false));
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
