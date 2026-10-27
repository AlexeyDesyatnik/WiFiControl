using System.Globalization;

namespace WiFiControl.Core.Model;

/// <summary>
/// Проверка введённых настроек профиля. Заодно приводит их к единому виду: маску к 255.255.255.0,
/// адреса — без ведущих нулей, которые Windows понимает как восьмеричную запись.
/// Перенесено из NetSwitch без уточняемого адреса.
/// </summary>
public static class ProfileRules
{
    public static List<string> Check(Profile profile)
    {
        var errors = new List<string>();

        profile.Name = profile.Name.Trim();
        if (profile.Name.Length == 0) errors.Add("Укажите название профиля.");

        errors.AddRange(CheckIpv4(profile.Ipv4));
        errors.AddRange(CheckProxy(profile.Proxy));
        return errors;
    }

    public static List<string> CheckIpv4(Ipv4Settings settings)
    {
        var errors = new List<string>();

        if (settings.AutoAddress)
        {
            settings.Address = settings.Mask = settings.Gateway = "";
        }
        else
        {
            settings.AutoDns = false;
            settings.Address = settings.Address.Trim();
            settings.Gateway = settings.Gateway.Trim();

            if (Normalize(settings.Address, out string address))
                settings.Address = address;
            else
                errors.Add("Адрес IPv4 задан неверно. Пример: 10.68.12.37");

            if (Ipv4.TryParseMask(settings.Mask, out string mask) && mask != "0.0.0.0")
                settings.Mask = mask;
            else
                errors.Add("Маска задана неверно. Примеры: 255.255.255.0 или /24");

            if (settings.Gateway.Length > 0)
            {
                if (Normalize(settings.Gateway, out string gateway))
                    settings.Gateway = gateway;
                else
                    errors.Add("Шлюз задан неверно. Пример: 10.68.12.1");
            }
        }

        if (settings.AutoDns)
        {
            settings.Dns.Clear();
            return errors;
        }

        var servers = new List<string>();
        foreach (string entry in settings.Dns)
        {
            if (entry.Trim().Length == 0) continue;

            if (Normalize(entry, out string server))
                servers.Add(server);
            else
                errors.Add("DNS-сервер «" + entry.Trim() + "» задан неверно. Пример: 10.0.68.10");
        }
        settings.Dns.Clear();
        settings.Dns.AddRange(servers);

        if (servers.Count > 2) errors.Add("DNS-серверов может быть не больше двух.");
        if (settings.AutoAddress && servers.Count == 0 && errors.Count == 0)
            errors.Add("Укажите DNS-сервер или выберите автоматическое получение DNS.");

        return errors;
    }

    /// <summary>Флажки локальных адресов и частных сетей проверка не трогает: в исключения они превращаются только при записи в систему.</summary>
    public static List<string> CheckProxy(ProxySettings settings)
    {
        var errors = new List<string>();
        settings.Server = settings.Server.Trim();

        if (settings.Enabled)
        {
            if (settings.Server.Length == 0)
                errors.Add("Укажите прокси-сервер. Пример: 10.0.68.52:3128");
            else if (settings.Server.AsSpan().IndexOfAny(' ', '\t') >= 0)
                errors.Add("В адресе прокси-сервера не должно быть пробелов.");
            else if (!HasPort(settings.Server))
                errors.Add("Укажите порт прокси-сервера. Пример: 10.0.68.52:3128");
        }
        return errors;
    }

    private static bool HasPort(string server)
    {
        int colon = server.LastIndexOf(':');
        if (colon <= 0 || colon == server.Length - 1) return false;

        return int.TryParse(server.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            && port >= 1 && port <= 65535;
    }

    private static bool Normalize(string text, out string address)
    {
        address = "";
        if (!Ipv4.TryParse(text, out uint value)) return false;

        address = Ipv4.Format(value);
        return true;
    }
}
