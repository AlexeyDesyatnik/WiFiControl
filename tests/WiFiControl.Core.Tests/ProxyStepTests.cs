using WiFiControl.Core.Model;

namespace WiFiControl.Core.Tests;

public class ProxyStepTests
{
    private readonly FakeSystem _system = new();

    public ProxyStepTests()
    {
        // Человек раньше пользовался сценарием и автоопределением, а в WinHTTP — другой прокси
        _system.SystemProxy = new SystemProxyState
        {
            Settings = ProxySettings.FromRaw(true, "old:8080", "a.local;<local>"),
            AutoDetect = true,
            AutoConfigUrl = "http://wpad/proxy.pac"
        };
        _system.WinHttp = ProxySettings.FromRaw(true, "other:8080", "");
    }

    [Fact]
    public async Task Enabled_proxy_goes_to_both_proxies_alike_with_auto_detect_and_script_off()
    {
        ProxySettings proxy = ProxySettings.FromRaw(true, "10.0.68.52:3128", "*.corp.local;<local>");

        StepResult result = await new ProxyStep(_system, proxy).RunAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("Настройки прокси", result.Title);
        Assert.Equal(["system proxy", "winhttp"], _system.Calls);
        Assert.True(_system.SystemProxy.Settings.SameAs(proxy));
        Assert.True(_system.WinHttp.SameAs(proxy));
        Assert.False(_system.SystemProxy.AutoDetect);
        Assert.Equal("", _system.SystemProxy.AutoConfigUrl);
    }

    [Fact]
    public async Task Private_networks_flag_reaches_both_proxies()
    {
        var proxy = new ProxySettings { Enabled = true, Server = "proxy:3128", PrivateNetworksDirect = true };

        await new ProxyStep(_system, proxy).RunAsync(CancellationToken.None);

        Assert.True(_system.SystemProxy.Settings.PrivateNetworksDirect);
        Assert.True(_system.WinHttp.PrivateNetworksDirect);
    }

    [Fact]
    public async Task Turning_proxy_off_keeps_the_remembered_server_and_makes_winhttp_direct()
    {
        StepResult result = await new ProxyStep(_system, new ProxySettings()).RunAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.False(_system.SystemProxy.Settings.Enabled);
        Assert.Equal("old:8080", _system.SystemProxy.Settings.Server);
        Assert.Equal(["a.local"], _system.SystemProxy.Settings.Exceptions);
        Assert.True(_system.SystemProxy.Settings.BypassLocal);
        Assert.False(_system.SystemProxy.AutoDetect);
        Assert.Equal("", _system.SystemProxy.AutoConfigUrl);
        Assert.False(_system.WinHttp.Enabled);
        Assert.Equal("", _system.WinHttp.Server);
        Assert.Equal("", _system.WinHttp.ToBypassList());
    }

    [Fact]
    public async Task Proxy_is_still_turned_off_when_the_remembered_one_cannot_be_read()
    {
        _system.ProxyReadFailure = new InvalidOperationException("Windows не отдала настройки прокси: отказ.");

        StepResult result = await new ProxyStep(_system, new ProxySettings()).RunAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.False(_system.SystemProxy.Settings.Enabled);
        Assert.False(_system.WinHttp.Enabled);
    }

    [Fact]
    public async Task Failed_system_proxy_does_not_stop_winhttp()
    {
        _system.SystemProxyFailure = new InvalidOperationException("Windows не дала записать системный прокси: отказано в доступе.");
        ProxySettings proxy = ProxySettings.FromRaw(true, "p:3128", "");

        StepResult result = await new ProxyStep(_system, proxy).RunAsync(CancellationToken.None);

        Assert.Equal("Windows не дала записать системный прокси: отказано в доступе.", result.Error);
        Assert.Equal(["system proxy", "winhttp"], _system.Calls);
        Assert.Equal("p:3128", _system.WinHttp.Server);
    }

    [Fact]
    public async Task Unexpected_system_proxy_error_does_not_stop_winhttp_either()
    {
        _system.SystemProxyFailure = new ArgumentException("ошибка в коде");

        StepResult result = await new ProxyStep(_system, ProxySettings.FromRaw(true, "p:3128", "")).RunAsync(CancellationToken.None);

        Assert.Equal("Непредвиденная ошибка (ArgumentException): ошибка в коде", result.Error);
        Assert.Equal("p:3128", _system.WinHttp.Server);
    }

    [Fact]
    public async Task Both_failures_are_reported()
    {
        _system.SystemProxyFailure = new InvalidOperationException("Системный: отказ.");
        _system.WinHttpFailure = new InvalidOperationException("WinHTTP: отказ.");

        StepResult result = await new ProxyStep(_system, ProxySettings.FromRaw(true, "p:3128", "")).RunAsync(CancellationToken.None);

        Assert.Equal("Системный: отказ. WinHTTP: отказ.", result.Error);
    }

    [Fact]
    public async Task Step_does_not_share_settings_with_the_profile()
    {
        var profile = new Profile { Proxy = ProxySettings.FromRaw(true, "p:3128", "") };
        var step = new ProxyStep(_system, profile.Proxy);
        profile.Proxy.Server = "changed:3128";

        await step.RunAsync(CancellationToken.None);

        Assert.Equal("p:3128", _system.SystemProxy.Settings.Server);
    }

    [Fact]
    public async Task Cancelled_step_is_not_a_failed_step()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProxyStep(_system, new ProxySettings()).RunAsync(cancelled.Token));
        Assert.Empty(_system.Calls);
    }
}
