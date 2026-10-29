using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WiFiControl.Core.Model;
using WiFiControl.Core.Storage;

namespace WiFiControl;

/// <summary>
/// Главное окно: блок «Сейчас» и список сетей из снимка текущего состояния, «Применить профиль…»
/// и «Применить» у расхождения.
/// </summary>
internal sealed partial class MainViewModel(INetworkSystem network, IWifiSystem wifi, ProfileStore store, Applier applier) : ObservableObject
{
    private const string Missing = "—";

    private CurrentState? _state;
    private bool _loading;
    private bool _loadAgain;
    // Отказы, которые удачное чтение не отменяет: их видно, пока причина не уйдёт
    private string? _scanError;
    private string? _notificationsError;

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

    /// <summary>Идёт применение: остальные действия недоступны.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(ApplyProfileCommand), nameof(ApplyExpectedCommand), nameof(CloseStepsCommand))]
    private bool _isBusy;

    /// <summary>Заголовок хода применения; null, если хода не видно.</summary>
    [ObservableProperty]
    private string? _stepsTitle;

    /// <summary>Почему состояние не прочитано; null, если прочитано.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>Признаки проблем и расхождение.</summary>
    public ObservableCollection<ProblemViewModel> Problems { get; } = [];

    public ObservableCollection<NetworkRowViewModel> Networks { get; } = [];

    /// <summary>Ход последнего применения по шагам.</summary>
    public ObservableCollection<StepViewModel> Steps { get; } = [];

    /// <summary>Применить профиль можно: адаптер подключён к известной сети, профили прочитаны, применение не идёт.</summary>
    public bool CanApplyProfile => !IsBusy && _state?.Adapter != null && _state.Network != null && store.LoadError == null;

    /// <summary>Профили для «Применить профиль…»: «Автоматически» первым.</summary>
    public IReadOnlyList<Profile> ProfileChoices => store.LoadError == null ? [Profile.Automatic, .. store.Profiles.Profiles] : [];

    /// <summary>Подписка на уведомления Wi-Fi не удалась: окно обновляется только по таймеру и «Обновить».</summary>
    public string? NotificationsError
    {
        set
        {
            _notificationsError = value;
            Error = value;
        }
    }

    /// <summary>«Обновить»: поиск сетей, затем чтение.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    public async Task RefreshAsync()
    {
        try
        {
            IReadOnlyList<WifiAdapter> adapters = await wifi.ReadAdaptersAsync(CancellationToken.None);
            if (adapters.Count > 0) await wifi.ScanAsync(adapters[0].Id, CancellationToken.None);
            _scanError = null;
        }
        catch (InvalidOperationException e)
        {
            _scanError = e.Message;
        }
        await LoadAsync();
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
                try
                {
                    Show(await CurrentState.ReadAsync(network, wifi, store.LoadError == null ? store.Profiles : null, applier.Last, CancellationToken.None));
                    Error = _scanError ?? _notificationsError;
                }
                catch (InvalidOperationException e)
                {
                    Error = e.Message;
                }
            } while (_loadAgain);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>«Применить профиль…» для текущей сети: привязку не меняет.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyProfileAsync(Profile profile) => ApplyAsync(profile);

    /// <summary>«Применить» у расхождения: ожидаемый профиль заново.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyExpectedAsync() => _state?.Mismatch == null ? Task.CompletedTask : ApplyAsync(_state.Mismatch.Expected);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void CloseSteps()
    {
        StepsTitle = null;
        Steps.Clear();
    }

    private bool IsIdle() => !IsBusy;

    private bool CanApply() => CanApplyProfile;

    private async Task ApplyAsync(Profile profile)
    {
        if (!CanApplyProfile) return;
        CurrentState state = _state!;

        IsBusy = true;
        StepsTitle = "Применение профиля «" + profile.Name + "»";
        Steps.Clear();
        // Шаги приходят из чужого потока; Progress возвращает их в поток окна по порядку
        var progress = new Progress<StepResult>(result => Steps.Add(new StepViewModel(result)));
        try
        {
            await applier.ApplyProfileAsync(state.Adapter!, state.Network!, profile, ((IProgress<StepResult>)progress).Report, CancellationToken.None);
        }
        finally
        {
            IsBusy = false;
        }
        await LoadAsync();
    }

    private void Show(CurrentState state)
    {
        _state = state;
        OnPropertyChanged(nameof(CanApplyProfile));
        ApplyProfileCommand.NotifyCanExecuteChanged();
        ApplyExpectedCommand.NotifyCanExecuteChanged();

        AdapterName = state.WifiAdapter?.Description ?? "";
        NetworkTitle = state.WifiAdapter == null ? "Wi-Fi-адаптер не найден" : state.Network ?? "Не подключено";
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
        return new("Расхождение с профилем «" + mismatch.Expected.Name + "»",
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
    public string Name => row.Network;

    public int Signal => row.Signal;

    public bool IsCurrent => row.IsCurrent;

    public bool IsSecured => row.Security != WifiSecurity.Open;

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
