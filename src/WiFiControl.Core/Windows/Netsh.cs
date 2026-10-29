using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using WiFiControl.Core.Model;

namespace WiFiControl.Core.Windows;

// Перенесено из NetSwitch

/// <summary>
/// Настройки IPv4 меняются штатной утилитой netsh: в отличие от WMI она работает и тогда,
/// когда на адаптере нет соединения. См. ADR 0003.
/// </summary>
internal static class Netsh
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Команды, переводящие адаптер из текущих настроек в заданные. Для совпадающих частей команд нет.</summary>
    public static IReadOnlyList<string> Ipv4Commands(string adapterName, Ipv4Settings current, Ipv4Settings target)
    {
        var commands = new List<string>();
        string adapter = "name=\"" + adapterName + "\"";

        if (target.AutoAddress)
        {
            if (!current.AutoAddress) commands.Add("interface ipv4 set address " + adapter + " source=dhcp");
        }
        else if (current.AutoAddress || !SameAddress(current, target))
        {
            // Нет уверенности, что на всех версиях Windows netsh заменит прежний шлюз, а не оставит
            // его рядом с новым. Проход через автополучение надёжно стирает всё заданное вручную.
            if (!current.AutoAddress && !Ipv4.Same(current.Gateway, target.Gateway))
            {
                commands.Add("interface ipv4 set address " + adapter + " source=dhcp");
            }
            commands.Add("interface ipv4 set address " + adapter + " source=static address=" + target.Address
                + " mask=" + target.Mask + " gateway=" + (target.Gateway.Length > 0 ? target.Gateway : "none"));
        }

        if (target.AutoDns)
        {
            if (!current.AutoDns) commands.Add("interface ipv4 set dnsservers " + adapter + " source=dhcp");
        }
        else if (current.AutoDns || !SameDns(current, target))
        {
            // validate=no: без соединения проверка сервера только задерживает и даёт ложную ошибку
            commands.Add("interface ipv4 set dnsservers " + adapter + " source=static address="
                + (target.Dns.Count > 0 ? target.Dns[0] : "none") + " validate=no");
            for (int i = 1; i < target.Dns.Count; i++)
            {
                commands.Add("interface ipv4 add dnsservers " + adapter + " address=" + target.Dns[i]
                    + " index=" + (i + 1).ToString(CultureInfo.InvariantCulture) + " validate=no");
            }
        }

        return commands;
    }

    /// <summary>
    /// Команды, стирающие записанные на адаптере ручные адреса и шлюзы. Нужны там, где «source=dhcp»
    /// не срабатывает: на адаптере без соединения ручные настройки ждут подключения, а DHCP формально включён.
    /// </summary>
    public static IReadOnlyList<string> DeleteAddressCommands(string adapterName, TcpipInterfaceValues values)
    {
        var commands = new List<string>();
        foreach (string address in values.IpAddress)
        {
            if (TcpipRegistry.IsBlank(address)) continue;

            // Шлюзы удаляются один раз, вместе с первым адресом
            commands.Add("interface ipv4 delete address name=\"" + adapterName + "\" address=" + address.Trim()
                + (commands.Count == 0 ? " gateway=all" : ""));
        }
        return commands;
    }

    /// <summary>
    /// Маршрут по умолчанию через адаптер только в действующих настройках: в постоянных он уже записан вместе
    /// со статическим шлюзом, но после первого подключения может пропасть (ADR 0004).
    /// </summary>
    public static string AddDefaultRouteCommand(string adapterName, string gateway)
    {
        return "interface ipv4 add route prefix=0.0.0.0/0 interface=\"" + adapterName + "\" nexthop=" + gateway + " store=active";
    }

    /// <summary>Выполняет команду скрытым процессом и возвращает описание сбоя; null, если netsh завершился успешно.</summary>
    public static async Task<string?> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        // netsh пишет в кодировке консоли, а не в ANSI
        Encoding encoding = ConsoleEncoding();
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding
        };

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить netsh.");
        }
        catch (Win32Exception e)
        {
            throw new InvalidOperationException("Не удалось запустить netsh: " + e.Message, e);
        }

        using (process)
        {
            // Потоки закрываются, когда процесс завершается или его останавливают
            Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> error = process.StandardError.ReadToEndAsync(CancellationToken.None);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Stop(process);
                // Чтение потоков завершится вместе с процессом; дожидаемся его, чтобы сбой чтения не остался незамеченным
                await Task.WhenAll(output, error).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return "netsh не завершился за " + Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " секунд.";
            }

            string text = ((await output.ConfigureAwait(false)) + (await error.ConfigureAwait(false))).Trim();
            if (process.ExitCode == 0) return null;
            return text.Length > 0 ? text : "netsh завершился с кодом " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ".";
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Уже завершился
        }
        catch (Win32Exception)
        {
            // Завершается и остановить себя не даёт
        }
    }

    private static Encoding ConsoleEncoding()
    {
        // Кодовые страницы консоли (866 и подобные) в .NET доступны только через этот поставщик
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding((int)GetOEMCP());
    }

    private static bool SameAddress(Ipv4Settings one, Ipv4Settings other)
    {
        return Ipv4.Same(one.Address, other.Address) && Ipv4.Same(one.Mask, other.Mask) && Ipv4.Same(one.Gateway, other.Gateway);
    }

    private static bool SameDns(Ipv4Settings one, Ipv4Settings other)
    {
        if (one.Dns.Count != other.Dns.Count) return false;
        for (int i = 0; i < one.Dns.Count; i++)
        {
            if (!Ipv4.Same(one.Dns[i], other.Dns[i])) return false;
        }
        return true;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();
}
