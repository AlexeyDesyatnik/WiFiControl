namespace WiFiControl.Recon.Model;

/// <summary>
/// Записи о доступе к местоположению в разделе реестра <c>CapabilityAccessManager\ConsentStore\location</c>
/// пользователя или компьютера.
/// </summary>
/// <param name="Location">Сам раздел <c>location</c>: общий переключатель над доступом к местоположению; null — раздела нет.</param>
/// <param name="DesktopApps">Подраздел <c>NonPackaged</c>: сам доступ к местоположению («Разрешить классическим приложениям доступ к местоположению»); null — подраздела нет.</param>
/// <param name="DesktopAppUsages">Подразделы <c>NonPackaged</c> — классические приложения, которые обращались к местоположению.</param>
/// <param name="Prompts">Подразделы <c>NonPackaged\Executables</c> — exe, которым Windows показывала запрос о доступе.</param>
internal sealed record LocationConsent(
    ConsentSwitch? Location, ConsentSwitch? DesktopApps, IReadOnlyList<DesktopAppUsage> DesktopAppUsages, IReadOnlyList<LocationPrompt> Prompts)
{
    /// <summary>Путь раздела внутри HKLM и раздела пользователя в HKEY_USERS.</summary>
    public const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location";
}

/// <summary>Переключатель доступа: значения <c>Value</c> и <c>LastSetTime</c> раздела реестра.</summary>
/// <param name="Value">Обычно <c>Allow</c> или <c>Deny</c>; null — значения нет.</param>
/// <param name="LastSet">Когда переключатель меняли; null — значения нет.</param>
internal sealed record ConsentSwitch(string? Value, DateTime? LastSet);

/// <summary>Запись классического приложения: когда оно обращалось к местоположению.</summary>
/// <param name="Path">Путь к exe; в имени раздела реестра Windows пишет «#» вместо «\».</param>
/// <param name="LastUsedStart">Начало последнего обращения; null — не записано.</param>
/// <param name="LastUsedStop">Конец последнего обращения; null — не записан, например обращение ещё идёт.</param>
internal sealed record DesktopAppUsage(string Path, DateTime? LastUsedStart, DateTime? LastUsedStop);

/// <summary>
/// Запись подраздела <c>NonPackaged\Executables</c> для exe. Похоже, так Windows отмечает показанный запрос о доступе
/// к местоположению, но это не проверено — проверяет сценарий 6.
/// </summary>
/// <param name="Executable">Имя exe без пути.</param>
/// <param name="GlobalPromptShown">Значение <c>GlobalPromptShown</c>, на проверочной машине 1; null — значения нет.</param>
internal sealed record LocationPrompt(string Executable, int? GlobalPromptShown);
