namespace WiFiControl.Core.Model;

/// <summary>В чём фактические настройки отличаются от ожидаемого профиля.</summary>
public enum Difference
{
    /// <summary>Способ получения адреса, адрес, маска или шлюз.</summary>
    Address,
    /// <summary>Способ получения DNS или список серверов.</summary>
    Dns,
    /// <summary>Системный прокси.</summary>
    SystemProxy,
    /// <summary>WinHTTP-прокси.</summary>
    WinHttpProxy,
    /// <summary>В системном прокси включено автоопределение: профиль его выключает.</summary>
    ProxyAutoDetect,
    /// <summary>В системном прокси задан сценарий (PAC): профиль его убирает.</summary>
    ProxyScript,
}

/// <summary>Расхождение: сравнение фактических настроек с ожидаемым профилем.</summary>
public static class Mismatch
{
    /// <summary>Профиль последнего применения, если адаптер всё ещё в той же сети, иначе — профиль текущей сети.</summary>
    /// <param name="network">Ключ текущей сети.</param>
    public static Profile ExpectedProfile(LastApplication? last, string network, ProfileSet profiles)
    {
        return last != null && NetworkKey.Comparer.Equals(last.Network, network) ? last.Profile : profiles.ProfileFor(network);
    }

    /// <param name="configured">Настройки IPv4, настроенные на адаптере, а не полученные: адрес по DHCP — не расхождение с автоматическим.</param>
    /// <returns>Различия по порядку <see cref="Difference"/>; пусто, если расхождения нет.</returns>
    public static IReadOnlyList<Difference> Find(Profile expected, Ipv4Settings configured, ProxyState proxy)
    {
        var differences = new List<Difference>();
        if (!expected.Ipv4.SameAddressAs(configured)) differences.Add(Difference.Address);
        if (!expected.Ipv4.SameDnsAs(configured)) differences.Add(Difference.Dns);
        // Выключенные прокси одинаковы при любых сервере и исключениях: в системном их оставляет само применение
        if (!expected.Proxy.SameAs(proxy.SystemProxy.Settings)) differences.Add(Difference.SystemProxy);
        if (!expected.Proxy.SameAs(proxy.WinHttp)) differences.Add(Difference.WinHttpProxy);
        if (proxy.SystemProxy.AutoDetect) differences.Add(Difference.ProxyAutoDetect);
        if (proxy.SystemProxy.AutoConfigUrl.Length > 0) differences.Add(Difference.ProxyScript);
        return differences;
    }
}
