namespace WiFiControl.Recon.Model;

/// <param name="ProductName">Как Windows называет себя в реестре; Windows 11 называет себя там «Windows 10».</param>
/// <param name="DisplayVersion">Выпуск вида «24H2»; у старых Windows 10 его нет.</param>
/// <param name="Revision">Номер обновления сборки (UBR).</param>
internal sealed record WindowsVersion(string ProductName, string? Edition, string? DisplayVersion, int Build, int Revision)
{
    // Windows 11 отличается от Windows 10 только номером сборки
    public bool IsWindows11 => Build >= 22000;
}

/// <param name="IsAdministrator">Процесс работает с правами администратора (повышенный маркер).</param>
internal sealed record ProcessIdentity(string User, bool IsAdministrator);

/// <param name="Name">Имя без «.exe», как его отдаёт Windows; null, если процесс уже завершился.</param>
internal sealed record ParentProcess(int Id, string? Name);

/// <summary>
/// Задачу Планировщика запускает служба Schedule, поэтому у программы, запущенной из Планировщика,
/// родитель — процесс этой службы.
/// </summary>
/// <param name="Parent">null, если номер родителя прочитать не удалось.</param>
/// <param name="SchedulerProcessId">Процесс службы Schedule; null, если служба не запущена.</param>
internal sealed record LaunchOrigin(ParentProcess? Parent, int? SchedulerProcessId)
{
    /// <summary>null — определить нельзя: родитель неизвестен или уже завершился.</summary>
    public bool? FromScheduler
    {
        get
        {
            if (Parent == null || Parent.Name == null) return null;
            return Parent.Id == SchedulerProcessId;
        }
    }
}
