namespace WiFiControl.Core.Model;

/// <summary>
/// Шаг применения «настройки прокси»: одни настройки идут и в системный прокси, и в WinHTTP-прокси.
/// Автоопределение и сценарий (PAC) выключаются, чтобы результат был однозначным (ADR 0003).
/// Перенесено из NetSwitch.
/// </summary>
public sealed class ProxyStep
{
    private readonly INetworkSystem _system;
    private readonly ProxySettings _settings;

    public ProxyStep(INetworkSystem system, ProxySettings settings)
    {
        _system = system;
        _settings = settings.Clone();
    }

    public string Title => "Настройки прокси";

    public Task<StepResult> RunAsync(CancellationToken cancellationToken)
    {
        return StepRunner.RunAsync(Title, WriteAsync, cancellationToken);
    }

    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        var system = new SystemProxyState { Settings = await SystemProxySettingsAsync(cancellationToken).ConfigureAwait(false) };
        ProxySettings winHttp = _settings.Enabled ? _settings.Clone() : new ProxySettings();

        // Сбой одного прокси, даже непредвиденный, не повод оставить второй со старыми настройками
        StepResult[] results =
        [
            await StepRunner.RunAsync(Title, token => _system.WriteSystemProxyAsync(system, token), cancellationToken).ConfigureAwait(false),
            await StepRunner.RunAsync(Title, token => _system.WriteWinHttpProxyAsync(winHttp, token), cancellationToken).ConfigureAwait(false)
        ];
        if (results.Any(result => !result.Ok)) throw new InvalidOperationException(string.Join(" ", results.Where(result => !result.Ok).Select(result => result.Error)));
    }

    /// <summary>
    /// Выключенный прокси сохраняет в системном прежний сервер и исключения с выключенным флагом:
    /// человек включит его галочкой в Windows, не вводя адрес заново.
    /// </summary>
    private async Task<ProxySettings> SystemProxySettingsAsync(CancellationToken cancellationToken)
    {
        if (_settings.Enabled) return _settings.Clone();

        ProxySettings previous;
        try
        {
            previous = (await _system.ReadProxyAsync(cancellationToken).ConfigureAwait(false)).SystemProxy.Settings;
        }
        // Прежний сервер — удобство, а выключить прокси нужно при любом сбое чтения: без этого в сети без прокси нет интернета
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            previous = _settings.Clone();
        }
        previous.Enabled = false;
        return previous;
    }
}
