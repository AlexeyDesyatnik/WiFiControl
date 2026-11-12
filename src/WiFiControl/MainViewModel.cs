using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WiFiControl.Core.Model;
using WiFiControl.Core.Storage;

namespace WiFiControl;

/// <summary>
/// Главное окно: блок «Сейчас» и список сетей из снимка текущего состояния, «Подключиться» у знакомой сети
/// и у новой, «Ввести пароль заново», «Применить профиль…» и «Применить» у расхождения.
/// </summary>
internal sealed partial class MainViewModel(INetworkSystem network, IWifiSystem wifi, ProfileStore store, StateStore stateStore, Applier applier)
    : ObservableObject
{
    private const string Missing = "—";

    private CurrentState? _state;
    private ProfileSet? _displayedProfiles;
    private Guid? _preferredWifiAdapter = stateStore.Load();
    private Guid? _displayedWifiPreference;
    private bool _stateReadFailed;
    private bool _loading;
    private bool _loadAgain;
    // Отказы, которые удачное чтение не отменяет: их видно, пока причина не уйдёт
    private string? _scanError;
    private string? _notificationsError;
    private string? _autoConnectError;
    private string? _adapterSaveError;
    private string? _bindingError;

    [ObservableProperty]
    private string _adapterName = "";

    [ObservableProperty]
    private string _networkTitle = "";

    [ObservableProperty]
    private string _networkProfile = "";

    [ObservableProperty]
    private int _signal;

    [ObservableProperty]
    private string _address = Missing;

    [ObservableProperty]
    private string _gateway = Missing;

    [ObservableProperty]
    private string _dns = Missing;

    [ObservableProperty]
    private string _proxy = Missing;

    /// <summary>Идёт запись настроек: остальные действия недоступны, прервать нельзя.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProfiles))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(CloseStepsCommand), nameof(ReloadProfilesCommand))]
    private bool _isBusy;

    partial void OnIsBusyChanged(bool value) => NotifyNetworkActions();

    /// <summary>Заголовок хода применения; null, если хода не видно.</summary>
    [ObservableProperty]
    private string? _stepsTitle;

    /// <summary>
    /// Сеть, к которой не подключились, потому что Windows отвергла пароль: у хода переключения есть «Ввести пароль заново».
    /// null, если такого нет.
    /// </summary>
    [ObservableProperty]
    private PasswordRetry? _passwordRejected;

    /// <summary>Почему состояние не прочитано; null, если прочитано.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>Признаки проблем и расхождение.</summary>
    public ObservableCollection<ProblemViewModel> Problems { get; } = [];

    public ObservableCollection<NetworkRowViewModel> Networks { get; } = [];

    public IReadOnlyList<WifiAdapter> WifiAdapters => _state?.WifiAdapters ?? [];

    public WifiAdapter? SelectedWifiAdapter => _state?.WifiAdapter;

    public bool HasAdapterChoice => WifiAdapters.Count > 1;

    /// <summary>Ход последнего применения по шагам.</summary>
    public ObservableCollection<StepViewModel> Steps { get; } = [];

    public bool LocationAccessDenied => _state?.LocationAccessDenied == true;

    public string? ProfileLoadError => store.LoadError;

    private ProfileSet? ReadableProfiles => ProfileLoadError == null ? store.Profiles : null;

    public bool CanEditProfiles => !IsBusy && ReadableProfiles != null;

    public ProfileSet ProfilesForEditor => store.Profiles;

    public string? CurrentNetworkForEditor => _state?.Network;

    /// <summary>Для действий с профилем сети список должен соответствовать прочитанному файлу профилей.</summary>
    public bool CanUseNetworkProfiles => CanUseWifi && CanEditProfiles && ReferenceEquals(_displayedProfiles, ReadableProfiles);

    /// <summary>Применить профиль можно: адаптер подключён к известной сети, профили прочитаны, применение не идёт.</summary>
    public bool CanApplyProfile => CanSwitch && _state?.Network != null;

    /// <summary>Переключаться можно: адаптер известен, профили прочитаны, ничего не идёт.</summary>
    public bool CanSwitch => CanUseNetworkProfiles && _state?.Adapter != null;

    public bool CanUseWifi => !IsBusy && !_stateReadFailed && _displayedWifiPreference == _preferredWifiAdapter
        && _state is { WifiAdapter: { IsRadioOn: not false }, NetworkReadError: null, LocationAccessDenied: false };

    /// <summary>Профили для меню в строке сети: «Автоматически» первым.</summary>
    public IReadOnlyList<Profile> ProfileChoices => CanUseNetworkProfiles ? [Profile.Automatic, .. store.Profiles.Profiles] : [];

    /// <summary>Подписка на уведомления Wi-Fi не удалась: окно обновляется только по таймеру и «Обновить».</summary>
    public string? NotificationsError
    {
        set => _notificationsError = value;
    }

    [RelayCommand]
    private void OpenLocationSettings() =>
        OpenInShell("ms-settings:privacy-location", "Не удалось открыть параметры местоположения");

    [RelayCommand]
    private void OpenProfilesFolder() =>
        OpenInShell(Path.GetDirectoryName(store.FilePath)!, "Не удалось открыть папку файла профилей");

    private void OpenInShell(string target, string failure)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Error = failure + ": " + e.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ReloadProfilesAsync()
    {
        if (IsBusy) return;
        _bindingError = null;
        store.Load();
        OnPropertyChanged(nameof(ProfileLoadError));
        OnPropertyChanged(nameof(CanEditProfiles));
        NotifyNetworkActions();
        await LoadAsync();
    }

    /// <summary>«Обновить»: поиск сетей, затем чтение.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    public async Task RefreshAsync()
    {
        _autoConnectError = _bindingError = null;
        Guid? preference = _preferredWifiAdapter;
        string? scanError = null;
        try
        {
            IReadOnlyList<WifiAdapter> adapters = await wifi.ReadAdaptersAsync(CancellationToken.None);
            WifiAdapter? adapter = CurrentState.SelectWifiAdapter(adapters, preference);
            if (preference == _preferredWifiAdapter && adapter is { IsRadioOn: not false })
                await wifi.ScanAsync(adapter.Id, CancellationToken.None);
        }
        catch (InvalidOperationException e)
        {
            scanError = e.Message;
        }
        if (preference == _preferredWifiAdapter) _scanError = scanError;
        await LoadAsync();
    }

    public bool TrySelectWifiAdapter(WifiAdapter adapter)
    {
        if (IsBusy || !HasAdapterChoice || !WifiAdapters.Any(candidate => candidate.Id == adapter.Id)) return false;
        try
        {
            stateStore.Save(adapter.Id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = _adapterSaveError = "Не удалось сохранить выбор Wi-Fi-адаптера: " + e.Message;
            return false;
        }

        _preferredWifiAdapter = adapter.Id;
        _adapterSaveError = _autoConnectError = _scanError = _bindingError = null;
        PasswordRejected = null;
        NotifyNetworkActions();
        return true;
    }

    /// <summary>
    /// Перечитывает состояние без поиска сетей. Вызов во время чтения откладывается до его конца,
    /// во время применения пропускается: применение само перечитывает состояние в конце.
    /// </summary>
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        if (_loading)
        {
            _loadAgain = true;
            return;
        }
        _loading = true;
        try
        {
            do
            {
                _loadAgain = false;
                Guid? preference = _preferredWifiAdapter;
                ProfileSet? profiles = ReadableProfiles;
                try
                {
                    CurrentState state = await CurrentState.ReadAsync(network, wifi, profiles,
                        applier.Last, CancellationToken.None, preference);
                    if (IsBusy) return;
                    if (preference != _preferredWifiAdapter || !ReferenceEquals(profiles, ReadableProfiles)) continue;
                    _displayedWifiPreference = preference;
                    _displayedProfiles = profiles;
                    _stateReadFailed = false;
                    if (state.LocationAccessDenied) _scanError = null;
                    Show(state);
                    Error = _bindingError ?? _adapterSaveError ?? _autoConnectError ?? state.NetworkReadError ?? state.WindowsProfilesReadError
                        ?? (state.LocationAccessDenied ? null :
                            (state.WifiAdapter is { IsRadioOn: not false } ? _scanError : null) ?? _notificationsError);
                }
                catch (InvalidOperationException e)
                {
                    if (IsBusy || preference != _preferredWifiAdapter) continue;
                    _stateReadFailed = true;
                    NotifyNetworkActions();
                    Error = e.Message;
                }
            } while (_loadAgain);
        }
        finally
        {
            _loading = false;
        }
    }

    public async Task SetAutoConnectAsync(NetworkRowViewModel row, bool autoConnect)
    {
        if (!CanUseWifi || !row.HasAutoConnectSetting || SelectedWifiAdapter is not { } adapter) return;

        IsBusy = true;
        _autoConnectError = null;
        try
        {
            await wifi.SetAutoConnectAsync(adapter.Id, row.Row.WindowsProfileName, autoConnect, CancellationToken.None);
        }
        catch (InvalidOperationException e)
        {
            Error = _autoConnectError = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
        await LoadAsync();
    }

    public IReadOnlyList<string> SaveProfile(Profile profile)
    {
        if (!CanEditProfiles) return ["Редактирование профилей сейчас недоступно."];
        try
        {
            IReadOnlyList<string> errors = store.Edit(profiles => profiles.Find(profile.Id) == null
                ? profiles.Add(profile)
                : profiles.Update(profile));
            if (errors.Count == 0)
            {
                _bindingError = null;
                NotifyNetworkActions();
            }
            return errors;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ["Не удалось сохранить профиль. " + e.Message];
        }
    }

    public Task<string?> ChangeBindingAsync(string networkKey, Guid? profileId, Func<Profile, bool> confirmApply,
        Guid? requiredAdapterId = null)
    {
        ProfileSet profiles = store.Profiles;
        if (profileId is { } id && profiles.Find(id) == null)
            return Task.FromResult<string?>("Профиль для привязки больше недоступен.");
        Guid? previous = profiles.Bindings.TryGetValue(networkKey, out Guid boundId) ? boundId : null;
        if (previous == profileId) return Task.FromResult<string?>(null);
        return ChangeProfileSelectionAsync(
            current => store.ChangeBinding(networkKey, profileId, current), confirmApply, requiredAdapterId);
    }

    public Task<string?> ChangeDefaultProfileAsync(Guid id, Func<Profile, bool> confirmApply) =>
        ChangeProfileSelectionAsync(current => store.ChangeDefaultProfile(id, current), confirmApply);

    public Task<string?> RemoveProfileAsync(Guid id, Func<Profile, bool> confirmApply) =>
        ChangeProfileSelectionAsync(current => store.RemoveProfile(id, current), confirmApply);

    private async Task<string?> ChangeProfileSelectionAsync(Func<string?, bool> save, Func<Profile, bool> confirmApply,
        Guid? requiredAdapterId = null)
    {
        if (!CanEditProfiles || (requiredAdapterId != null &&
            (!CanUseNetworkProfiles || SelectedWifiAdapter?.Id != requiredAdapterId)))
            return "Редактирование профилей сейчас недоступно.";

        IsBusy = true;
        _bindingError = null;
        string? currentNetwork = null;
        Adapter? adapter = null;
        Profile? profileToApply = null;
        bool saved = false;
        try
        {
            Guid? adapterId = requiredAdapterId;
            string? readError = null;
            try
            {
                adapterId ??= await ReadSelectedWifiAdapterIdAsync();
                if (adapterId is { } id) currentNetwork = await ReadCurrentNetworkKeyAsync(id);
            }
            catch (WifiLocationAccessDeniedException) when (requiredAdapterId == null)
            {
                // Редактор доступен и без доступа к местоположению.
            }
            catch (InvalidOperationException e) when (requiredAdapterId == null)
            {
                readError = e.Message;
            }

            bool affectsCurrent = save(currentNetwork);
            saved = true;
            if (readError != null)
                _bindingError = "Изменения сохранены. Не удалось проверить текущую сеть. " + readError;
            if (affectsCurrent && currentNetwork != null && adapterId is { } currentAdapterId)
            {
                Profile profile = store.Profiles.ProfileFor(currentNetwork);
                if (confirmApply(profile))
                {
                    IReadOnlyList<Adapter> adapters = await network.ReadAdaptersAsync(CancellationToken.None);
                    adapter = adapters.FirstOrDefault(candidate => Guid.TryParse(candidate.Id, out Guid id) && id == currentAdapterId);
                    if (adapter == null)
                        _bindingError = "Изменения сохранены. Не удалось прочитать настройки Wi-Fi-адаптера для применения.";
                    else if (await ReadSelectedWifiAdapterIdAsync() != currentAdapterId
                        || !NetworkKey.Comparer.Equals(currentNetwork, await ReadCurrentNetworkKeyAsync(currentAdapterId)))
                        _bindingError = "Изменения сохранены. Текущая сеть или Wi-Fi-адаптер изменились, профиль не применён.";
                    else
                        profileToApply = profile;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _bindingError = "Не удалось сохранить изменения. " + e.Message;
        }
        catch (InvalidOperationException e)
        {
            _bindingError = saved ? "Изменения сохранены. Профиль не применён. " + e.Message : e.Message;
        }
        finally
        {
            IsBusy = false;
        }

        string? error = _bindingError;
        if (error != null) Error = error;
        if (profileToApply != null) await ApplyAsync(adapter!, currentNetwork!, profileToApply);
        else await LoadAsync();
        return error;
    }

    private async Task<Guid?> ReadSelectedWifiAdapterIdAsync()
    {
        IReadOnlyList<WifiAdapter> adapters = await wifi.ReadAdaptersAsync(CancellationToken.None);
        WifiAdapter? selected = CurrentState.SelectWifiAdapter(adapters, _preferredWifiAdapter);
        return selected is { IsRadioOn: not false } ? selected.Id : null;
    }

    private async Task<string?> ReadCurrentNetworkKeyAsync(Guid adapterId)
    {
        byte[]? ssid = await wifi.ReadCurrentNetworkAsync(adapterId, CancellationToken.None);
        return ssid == null ? null : NetworkKey.FromBytes(ssid);
    }

    /// <summary>«Применить профиль…» для текущей сети: привязку не меняет.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyChosenProfile))]
    private Task ApplyProfileAsync(ApplyProfileRequest request) =>
        FindApplicableProfile(request) is { } profile ? ApplyAsync(profile) : Task.CompletedTask;

    /// <summary>«Применить» у расхождения: ожидаемый профиль заново.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyExpectedAsync() => _state?.Mismatch == null ? Task.CompletedTask : ApplyAsync(_state.Mismatch.Expected);

    /// <summary>«Подключиться» у знакомой сети: переключение с её профилем.</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync(NetworkRowViewModel row)
    {
        if (!CanConnect(row)) return;
        Adapter adapter = _state!.Adapter!;
        NetworkRow network = row.Row;
        Profile profile = network.Profile!;
        await SwitchAsync(new PasswordRetry(network.Network, network.WindowsProfileName), profile,
            report => applier.SwitchAsync(adapter, network.Network, network.WindowsProfileName, profile, report, CancellationToken.None));
    }

    /// <summary>
    /// «Подключиться…» у новой сети: переключение с созданием профиля сети Windows. Пароль сюда приходит из окна
    /// ввода пароля и дальше профиля сети Windows не уходит.
    /// </summary>
    public Task ConnectNewAsync(NewNetwork request, string password)
    {
        if (!CanSwitch) return Task.CompletedTask;
        if (NetworkKey.Comparer.Equals(request.Name, _state!.Network))
        {
            Error = "Вы уже подключены к сети «" + request.Name + "».";
            return Task.CompletedTask;
        }
        Adapter adapter = _state!.Adapter!;
        Profile profile = store.Profiles.ProfileFor(request.Name);
        NewWindowsProfile windowsProfile = request.Hidden
            ? WindowsProfileXml.BuildHidden(request.Name, request.Security, password, request.AutoConnect)
            : WindowsProfileXml.Build(request.Name, request.Security, password, request.AutoConnect);
        return SwitchAsync(new PasswordRetry(request.Name, "", request), profile,
            report => applier.SwitchToNewAsync(adapter, request.Name, windowsProfile, profile, report, CancellationToken.None));
    }

    /// <summary>«Ввести пароль заново» у знакомой сети: новый пароль в её профиле сети Windows и переключение.</summary>
    public Task ReenterPasswordAsync(PasswordRetry retry, string password)
    {
        if (!CanSwitch) return Task.CompletedTask;
        Adapter adapter = _state!.Adapter!;
        Profile profile = store.Profiles.ProfileFor(retry.Network);
        return SwitchAsync(retry, profile,
            report => applier.SwitchWithPasswordAsync(adapter, retry.Network, retry.WindowsProfileName, password, profile, report, CancellationToken.None));
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void CloseSteps()
    {
        StepsTitle = null;
        Steps.Clear();
        PasswordRejected = null;
    }

    private bool IsIdle() => !IsBusy;

    private bool CanApply() => CanApplyProfile;

    private bool CanApplyChosenProfile(ApplyProfileRequest? request) => FindApplicableProfile(request) != null;

    private Profile? FindApplicableProfile(ApplyProfileRequest? request) =>
        CanApplyProfile && request != null && request.AdapterId == SelectedWifiAdapter?.Id
            ? store.Profiles.Find(request.ProfileId)
            : null;

    /// <summary>Подключиться можно к знакомой сети, кроме текущей, когда её профиль известен и ничего не идёт.</summary>
    private bool CanConnect(NetworkRowViewModel? row) => CanSwitch && row is { CanConnect: true } && row.Row.Profile != null;

    private Task ApplyAsync(Profile profile)
    {
        if (!CanApplyProfile) return Task.CompletedTask;
        CurrentState state = _state!;
        return ApplyAsync(state.Adapter!, state.Network!, profile);
    }

    private Task ApplyAsync(Adapter adapter, string networkKey, Profile profile) =>
        RunAsync("Применение профиля " + profile.Name,
            report => applier.ApplyProfileAsync(adapter, networkKey, profile, report, CancellationToken.None));

    /// <summary>Переключение; если Windows отвергла пароль, у хода появляется «Ввести пароль заново».</summary>
    private Task SwitchAsync(PasswordRetry retry, Profile profile, Func<Action<StepResult>, Task<SwitchResult>> run)
    {
        return RunAsync("Переключение на сеть «" + retry.Network + "», профиль " + profile.Name, async report =>
        {
            SwitchResult result = await run(report);
            if (result.PasswordRejected) PasswordRejected = retry;
        });
    }

    /// <summary>Применение или переключение с ходом по шагам; в конце состояние перечитывается.</summary>
    private async Task RunAsync(string title, Func<Action<StepResult>, Task> run)
    {
        IsBusy = true;
        StepsTitle = title;
        Steps.Clear();
        PasswordRejected = null;
        // Шаги приходят из чужого потока; Progress возвращает их в поток окна по порядку
        var progress = new Progress<StepResult>(result => Steps.Add(new StepViewModel(result)));
        try
        {
            await run(((IProgress<StepResult>)progress).Report);
        }
        finally
        {
            IsBusy = false;
        }
        await LoadAsync();
    }

    private void NotifyNetworkActions()
    {
        OnPropertyChanged(nameof(CanUseWifi));
        OnPropertyChanged(nameof(CanUseNetworkProfiles));
        OnPropertyChanged(nameof(CanApplyProfile));
        OnPropertyChanged(nameof(CanSwitch));
        ApplyProfileCommand.NotifyCanExecuteChanged();
        ApplyExpectedCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged();
    }

    private void Show(CurrentState state)
    {
        bool activeAdapterChanged = SelectedWifiAdapter?.Id != state.WifiAdapter?.Id;
        bool hadAdapterChoice = HasAdapterChoice;
        bool locationAccessChanged = LocationAccessDenied != state.LocationAccessDenied;
        _state = state;
        if (locationAccessChanged) OnPropertyChanged(nameof(LocationAccessDenied));
        if (activeAdapterChanged) PasswordRejected = null;
        if (hadAdapterChoice != HasAdapterChoice) OnPropertyChanged(nameof(HasAdapterChoice));
        NotifyNetworkActions();
        AdapterName = state.WifiAdapter?.Description ?? "";
        NetworkTitle = state.WifiAdapter switch
        {
            null => "Wi-Fi-адаптер не найден",
            { IsRadioOn: false } => "Wi-Fi выключен",
            _ when state.LocationAccessDenied => "Сеть неизвестна",
            _ => state.Network ?? "Не подключено",
        };
        NetworkProfile = state.Network == null ? ""
            : state.Profile == null ? "профиль неизвестен"
            : state.IsBound ? "профиль: " + state.Profile.Name + " (привязана)"
            : "по умолчанию: " + state.Profile.Name;
        Signal = state.Networks.FirstOrDefault(row => row.IsCurrent)?.Signal ?? 0;

        ActiveIpv4? active = state.Adapter?.Active;
        Address = active == null || active.Addresses.Count == 0 ? Missing
            : string.Join(", ", active.Addresses) + (state.Adapter!.Configured.AutoAddress ? " · DHCP" : " · статический");
        Gateway = Join(active?.Gateways);
        Dns = Join(active?.Dns);
        Proxy = ProxyText(state.Proxy.SystemProxy);

        Problems.Clear();
        foreach (Problem problem in state.Problems) Problems.Add(ProblemViewModel.From(problem, state));
        if (state.Mismatch != null) Problems.Add(ProblemViewModel.FromMismatch(state.Mismatch, ApplyExpectedCommand));

        Networks.Clear();
        foreach (NetworkRow row in state.Networks) Networks.Add(new NetworkRowViewModel(row));
    }

    private static string Join(List<string>? values) => values == null || values.Count == 0 ? Missing : string.Join(", ", values);

    private static string ProxyText(SystemProxyState proxy)
    {
        var parts = new List<string>();
        if (proxy.Settings.Enabled)
        {
            parts.Add(proxy.Settings.Server);
            if (proxy.Settings.PrivateNetworksDirect) parts.Add("не для частных сетей");
        }
        if (proxy.AutoDetect) parts.Add("автоопределение");
        if (proxy.AutoConfigUrl.Length > 0) parts.Add("сценарий " + proxy.AutoConfigUrl);
        return parts.Count == 0 ? "нет" : string.Join(" · ", parts);
    }
}

internal sealed record NewNetwork(string Name, NewNetworkSecurity Security, bool Hidden = false, bool AutoConnect = true);

internal sealed record ApplyProfileRequest(Guid ProfileId, Guid AdapterId);

internal sealed record PasswordRetry(string Network, string WindowsProfileName, NewNetwork? NewNetwork = null);

/// <summary>Признак проблемы или расхождение в блоке «Сейчас».</summary>
/// <param name="Apply">«Применить» у расхождения; null у остальных признаков.</param>
internal sealed record ProblemViewModel(string Title, string Details, IRelayCommand? Apply = null)
{
    public static ProblemViewModel From(Problem problem, CurrentState state) => problem switch
    {
        Problem.NoAddress => new("Адрес не получен", "DHCP в сети не ответил: адреса нет или он 169.254.x.x."),
        Problem.AddressConflict => new("Конфликт адресов", "В сети есть другой компьютер с таким же адресом."),
        Problem.NoDefaultRoute => new("Нет маршрута по умолчанию", "Адрес есть, но маршрута в интернет через Wi-Fi-адаптер нет."),
        Problem.InternetViaEthernet => new("Интернет сейчас идёт через Ethernet",
            "Через адаптер «" + state.InternetAdapter?.Name + "»: настройки Wi-Fi на интернет не влияют."),
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, null),
    };

    public static ProblemViewModel FromMismatch(Mismatch mismatch, IRelayCommand apply)
    {
        return new("Расхождение с профилем " + mismatch.Expected.Name,
            "Отличаются: " + string.Join(", ", mismatch.Differences.Select(DifferenceText)) + ".", apply);
    }

    private static string DifferenceText(Difference difference) => difference switch
    {
        Difference.Address => "настройки адреса",
        Difference.Dns => "DNS",
        Difference.SystemProxy => "системный прокси",
        Difference.WinHttpProxy => "WinHTTP-прокси",
        Difference.ProxyAutoDetect => "включено автоопределение прокси",
        Difference.ProxyScript => "задан сценарий прокси",
        _ => throw new ArgumentOutOfRangeException(nameof(difference), difference, null),
    };
}

/// <summary>Шаг в ходе применения.</summary>
internal sealed class StepViewModel(StepResult result)
{
    public string Title => result.Title;

    public string Message => result.Message ?? "";

    public StepOutcome Outcome => result.Outcome;

    public string Icon => result.Outcome switch
    {
        StepOutcome.Done => "",
        StepOutcome.Warning => "",
        _ => "",
    };

    public string OutcomeText => result.Outcome switch
    {
        StepOutcome.Done => "Выполнено",
        StepOutcome.Warning => "Предупреждение",
        _ => "Ошибка",
    };
}

/// <summary>Строка списка сетей.</summary>
internal sealed class NetworkRowViewModel(NetworkRow row)
{
    public NetworkRow Row => row;

    public string Name => row.Network;

    public bool HasAutoConnectSetting => row.AutoConnect.HasValue;

    public bool DoNotAutoConnect => row.AutoConnect == false;

    /// <summary>У сети есть профиль сети Windows, и она не текущая: к ней можно подключиться.</summary>
    public bool CanConnect => !row.IsCurrent && row.WindowsProfileName.Length > 0;

    /// <summary>Новая сеть: профиля сети Windows нет, и она не текущая. «Подключиться…» с вводом пароля.</summary>
    public bool IsNew => !row.IsCurrent && row.WindowsProfileName.Length == 0;

    /// <summary>Защита для профиля сети Windows, который создаст инструмент; null — такой профиль только через системное меню.</summary>
    public NewNetworkSecurity? NewSecurity => WindowsProfileXml.SecurityFor(row.Security);

    /// <summary>Профиль сети Windows для новой сети инструмент не создаёт (802.1X, WEP и прочие).</summary>
    public bool IsUnsupported => NewSecurity == null;

    public int Signal => row.Signal;

    public bool IsCurrent => row.IsCurrent;

    public bool IsSecured => row.Security != WifiSecurity.Open;

    public Guid? BindingProfileId => row.IsBound ? row.Profile?.Id : null;

    public string Security => row.Security switch
    {
        WifiSecurity.Open => "Открытая сеть",
        WifiSecurity.Wep => "WEP",
        WifiSecurity.WpaPersonal => "WPA-Personal",
        WifiSecurity.Wpa2Personal => "WPA2-Personal",
        WifiSecurity.Wpa3Personal => "WPA3-Personal",
        WifiSecurity.Enterprise => "Корпоративная (802.1X)",
        _ => "Другая защита",
    };

    public string Profile => row.Profile == null ? "профиль неизвестен"
        : row.IsBound ? row.Profile.Name
        : "по умолчанию: " + row.Profile.Name;
}
