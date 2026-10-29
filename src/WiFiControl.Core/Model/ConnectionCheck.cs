using System.Globalization;

namespace WiFiControl.Core.Model;

/// <summary>
/// Проверка после применения: маршрут по умолчанию, адрес, шлюз, порт прокси. Неудачи — предупреждения, не ошибки
/// (ADR 0004). Маршрут не только проверяется, но и исправляется, и идёт первым: без него Windows через ~30 с рвёт соединение.
/// </summary>
internal sealed class ConnectionCheck(INetworkSystem system, TimeProvider time, Adapter adapter, Profile profile)
{
    // Срок маршрута — из ADR 0004 (при DHCP маршрут и адрес появлялись через 1,1–1,7 с после подключения);
    // остальные — предварительные числа из #29
    private static readonly TimeSpan RouteWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AddressWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(1);
    private const int PingAttempts = 3;
    private static readonly TimeSpan ProxyTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    private const string RouteTitle = "Маршрут по умолчанию";
    private const string AddressTitle = "Адрес";
    private const string GatewayTitle = "Шлюз";
    private const string ProxyTitle = "Порт прокси";

    private readonly Guid _adapterId = Guid.Parse(adapter.Id);

    public async Task RunAsync(Action<StepResult> report, CancellationToken cancellationToken)
    {
        // Статический адрес без шлюза — сеть без выхода наружу: ни маршрута, ни шлюза не ждём
        bool expectsGateway = profile.Ipv4.AutoAddress || profile.Ipv4.Gateway.Length > 0;
        if (expectsGateway) report(await CheckAsync(RouteTitle, CheckRouteAsync, cancellationToken).ConfigureAwait(false));
        report(await CheckAsync(AddressTitle, CheckAddressAsync, cancellationToken).ConfigureAwait(false));
        if (expectsGateway)
        {
            string gateway = await GatewayAsync(cancellationToken).ConfigureAwait(false);
            if (gateway.Length > 0) report(await CheckAsync(GatewayTitle, token => CheckGatewayAsync(gateway, token), cancellationToken).ConfigureAwait(false));
        }
        if (profile.Proxy.Enabled) report(await CheckAsync(ProxyTitle, CheckProxyAsync, cancellationToken).ConfigureAwait(false));
    }

    private async Task<StepResult> CheckRouteAsync(CancellationToken cancellationToken)
    {
        if (await WaitAsync(RouteWait, HasRouteAsync, cancellationToken).ConfigureAwait(false)) return new StepResult(RouteTitle, null);

        string gateway = await GatewayAsync(cancellationToken).ConfigureAwait(false);
        if (gateway.Length == 0) return StepResult.Warning(RouteTitle, "Маршрута по умолчанию нет, и шлюз неизвестен: DHCP не ответил.");
        try
        {
            await system.AddDefaultRouteAsync(adapter, gateway, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException e)
        {
            return StepResult.Warning(RouteTitle, "Маршрута по умолчанию нет. " + e.Message);
        }
        // Как с настройками IPv4, верим не коду возврата netsh, а прочитанному (ADR 0003)
        if (!await HasRouteAsync(cancellationToken).ConfigureAwait(false))
            return StepResult.Warning(RouteTitle, "Маршрута по умолчанию нет: Windows приняла команду, но маршрут через " + gateway + " не появился.");
        return new StepResult(RouteTitle, StepOutcome.Done, "Маршрута не было, добавлен через " + gateway + ".");
    }

    private async Task<bool> HasRouteAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DefaultRoute> routes = await system.ReadDefaultRoutesAsync(cancellationToken).ConfigureAwait(false);
        return routes.Any(route => route.Adapter == _adapterId);
    }

    private async Task<StepResult> CheckAddressAsync(CancellationToken cancellationToken)
    {
        if (await WaitAsync(AddressWait, HasAddressAsync, cancellationToken).ConfigureAwait(false)) return new StepResult(AddressTitle, null);

        if ((await ReadAdapterAsync(cancellationToken).ConfigureAwait(false)).Active.HasConflict)
        {
            return StepResult.Warning(AddressTitle, (profile.Ipv4.AutoAddress ? "Адрес" : "Адрес " + profile.Ipv4.Address)
                + " занят: в сети есть другой компьютер с таким же адресом.");
        }
        if (!profile.Ipv4.AutoAddress)
            return StepResult.Warning(AddressTitle, "Адрес " + profile.Ipv4.Address + " не появился на адаптере за " + Seconds(AddressWait) + " с.");
        return StepResult.Warning(AddressTitle, "Адрес не получен за " + Seconds(AddressWait) + " с: адреса нет или он 169.254.x.x.");
    }

    private async Task<bool> HasAddressAsync(CancellationToken cancellationToken)
    {
        // Адрес-дубликат в действующие не попадает: при конфликте адреса нет
        ActiveIpv4 active = (await ReadAdapterAsync(cancellationToken).ConfigureAwait(false)).Active;
        // Статический — именно адрес профиля: прежний адрес адаптера может ещё действовать
        if (!profile.Ipv4.AutoAddress) return active.Addresses.Any(address => Ipv4.Same(address.Split('/')[0], profile.Ipv4.Address));
        return active.Addresses.Any(address => !Ipv4.IsSelfAssigned(address));
    }

    private async Task<StepResult> CheckGatewayAsync(string gateway, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < PingAttempts; attempt++)
        {
            if (await system.PingAsync(gateway, PingTimeout, cancellationToken).ConfigureAwait(false)) return new StepResult(GatewayTitle, null);
        }
        return StepResult.Warning(GatewayTitle, "Шлюз " + gateway + " не ответил на ping: возможно, адрес не из подсети этой сети.");
    }

    private async Task<StepResult> CheckProxyAsync(CancellationToken cancellationToken)
    {
        string server = profile.Proxy.Server;
        int colon = server.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(server.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
            return StepResult.Warning(ProxyTitle, "Порт прокси не проверен: в адресе «" + server + "» нет порта.");

        if (await system.CanConnectAsync(server[..colon], port, ProxyTimeout, cancellationToken).ConfigureAwait(false)) return new StepResult(ProxyTitle, null);
        return StepResult.Warning(ProxyTitle, "Прокси " + server + " не принял соединение за " + Seconds(ProxyTimeout) + " с.");
    }

    /// <summary>Шлюз профиля, а при DHCP — полученный; пусто, если его нет.</summary>
    private async Task<string> GatewayAsync(CancellationToken cancellationToken)
    {
        if (!profile.Ipv4.AutoAddress) return profile.Ipv4.Gateway;
        return (await ReadAdapterAsync(cancellationToken).ConfigureAwait(false)).Active.Gateways.FirstOrDefault() ?? "";
    }

    private async Task<Adapter> ReadAdapterAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Adapter> adapters = await system.ReadAdaptersAsync(cancellationToken).ConfigureAwait(false);
        return adapters.FirstOrDefault(candidate => string.Equals(candidate.Id, adapter.Id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Адаптер «" + adapter.Name + "» больше не найден.");
    }

    /// <returns>true, если условие выполнилось до срока.</returns>
    private async Task<bool> WaitAsync(TimeSpan limit, Func<CancellationToken, Task<bool>> condition, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = time.GetUtcNow() + limit;
        while (!await condition(cancellationToken).ConfigureAwait(false))
        {
            if (time.GetUtcNow() >= deadline) return false;
            await Task.Delay(Poll, time, cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    /// <summary>Сбой чтения во время проверки — тоже предупреждение: применение уже сделано.</summary>
    private static async Task<StepResult> CheckAsync(string title, Func<CancellationToken, Task<StepResult>> check, CancellationToken cancellationToken)
    {
        StepResult? result = null;
        StepResult failure = await StepRunner.RunAsync(title, async token => result = await check(token).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
        return result ?? StepResult.Warning(title, failure.Error!);
    }

    private static string Seconds(TimeSpan span) => span.TotalSeconds.ToString(CultureInfo.InvariantCulture);
}
