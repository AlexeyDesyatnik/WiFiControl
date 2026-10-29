// ПРОТОТИП (#31): выдуманные данные для раскладки, ничего не читается из Windows.
namespace MainWindowLayout;

enum Severity { Info, Warning, Error }

enum ListState { Normal, NoLocation, FileNotRead, NoAdapter, RadioOff }

record Net(string Name, int Bars, string Security, string Profile, bool Bound, bool HasWindowsProfile, bool NoAutoConnect = false, bool Current = false)
{
    public bool Secured => Security != "Открытая";
}

record Problem(Severity Severity, string Text, string? Detail = null, bool Apply = false);

record Scenario(
    string Title,
    ListState State,
    string[] Adapters,
    string Network,
    string NetworkProfile,
    string Address,
    string Gateway,
    string Dns,
    string Proxy,
    Problem[] Problems,
    Net[] Nets)
{
    public bool CanSwitch => State == ListState.Normal;
    public bool CanBind => State is ListState.Normal or ListState.NoLocation;
    public bool CanEdit => State != ListState.FileNotRead;
    public Net? CurrentNet => Nets.FirstOrDefault(n => n.Current);
}

static class Data
{
    public static readonly string[] Profiles = ["Без привязки", "Автоматически", "Офис", "Дом", "Лаборатория"];

    static readonly Net[] Nets =
    [
        new("Office-5G", 4, "WPA2-Personal", "Офис", Bound: true, HasWindowsProfile: true, Current: true),
        new("HomeNet", 3, "WPA3-Personal", "Дом", Bound: true, HasWindowsProfile: true),
        new("Cafe_Free", 3, "Открытая", "Автоматически", Bound: false, HasWindowsProfile: false),
        new("Lab-Static", 2, "WPA2-Personal", "Лаборатория", Bound: true, HasWindowsProfile: true, NoAutoConnect: true),
        new("DIR-615-A7F2", 1, "WPA2-Personal", "Автоматически", Bound: false, HasWindowsProfile: false),
        new("Очень длинное имя сети соседа на третьем этаже", 1, "WPA2-Personal", "Автоматически", Bound: false, HasWindowsProfile: false),
    ];

    static readonly Problem Mismatch = new(Severity.Warning,
        "Настройки отличаются от профиля «Офис»",
        "DNS: 8.8.8.8 вместо 10.20.0.2, 10.20.0.3 · прокси выключен, в профиле proxy.corp.local:3128", Apply: true);

    public static readonly Scenario[] Scenarios =
    [
        new("Обычное: проблемы, два адаптера", ListState.Normal,
            ["Intel Wi-Fi 6E AX211", "TP-Link Archer T3U (USB)"],
            "Office-5G", "Офис (привязана)",
            "10.20.0.15/24 · статический", "10.20.0.1", "8.8.8.8", "нет",
            [
                Mismatch,
                new(Severity.Warning, "Интернет сейчас идёт через Ethernet", "Настройки Wi-Fi не влияют на доступ в интернет"),
                new(Severity.Error, "Нет маршрута по умолчанию через Wi-Fi-адаптер"),
            ],
            Nets),

        new("Всё в порядке, один адаптер", ListState.Normal,
            ["Intel Wi-Fi 6E AX211"],
            "Office-5G", "Офис (привязана)",
            "10.20.0.15/24 · статический", "10.20.0.1", "10.20.0.2, 10.20.0.3", "proxy.corp.local:3128 · не для частных сетей",
            [],
            Nets),

        new("Адрес не получен, конфликт", ListState.Normal,
            ["Intel Wi-Fi 6E AX211"],
            "Cafe_Free", "по умолчанию: Автоматически",
            "169.254.12.7 · DHCP", "—", "—", "нет",
            [
                new(Severity.Error, "Адрес не получен", "169.254.x.x: DHCP в сети не ответил"),
                new(Severity.Error, "Конфликт адресов", "10.20.0.15 уже занят другим устройством"),
            ],
            Nets.Select(n => n with { Current = n.Name == "Cafe_Free" }).OrderByDescending(n => n.Current).ToArray()),

        new("Нет доступа к местоположению", ListState.NoLocation,
            ["Intel Wi-Fi 6E AX211"],
            "неизвестно", "—",
            "10.20.0.15/24 · статический", "10.20.0.1", "10.20.0.2, 10.20.0.3", "proxy.corp.local:3128",
            [new(Severity.Warning, "Нет доступа к местоположению", "Без него Windows не сообщает имена сетей")],
            []),

        new("Файл профилей не прочитан", ListState.FileNotRead,
            ["Intel Wi-Fi 6E AX211"],
            "Office-5G", "—",
            "10.20.0.15/24 · статический", "10.20.0.1", "10.20.0.2, 10.20.0.3", "proxy.corp.local:3128",
            [],
            Nets.Select(n => n with { Profile = "?", Bound = false }).ToArray()),

        new("Wi-Fi-адаптер не найден", ListState.NoAdapter,
            [], "—", "—", "—", "—", "—", "нет", [], []),

        new("Wi-Fi выключен", ListState.RadioOff,
            ["Intel Wi-Fi 6E AX211"], "—", "—", "—", "—", "—", "proxy.corp.local:3128", [], []),
    ];

    public const string FileError = "Не удалось прочитать WiFiControl.json: ошибка в строке 12, позиция 7 — ожидалась запятая. Пока файл не исправлен, переключение и редактор недоступны.";
}
