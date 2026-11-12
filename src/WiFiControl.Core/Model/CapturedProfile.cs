namespace WiFiControl.Core.Model;

/// <summary>Черновик профиля из текущих настроек и признаки автоматической настройки прокси.</summary>
public sealed record CapturedProfile(Profile Profile, bool HasProxyAutoDetect, bool HasProxyScript)
{
    public static CapturedProfile FromSettings(string networkName, Adapter adapter, SystemProxyState proxy)
    {
        var profile = new Profile
        {
            Name = networkName,
            Ipv4 = adapter.Configured.Clone(),
            Proxy = proxy.Settings.Clone(),
        };
        return new CapturedProfile(profile, proxy.AutoDetect, proxy.AutoConfigUrl.Length > 0);
    }
}
