using WiFiControl.Recon.Model;

namespace WiFiControl.Recon.Tests;

/// <summary>Компьютер в памяти: что задано в полях, то и прочитается; заданный отказ бросается при чтении.</summary>
internal sealed class FakeSystem : IReadOnlySystem
{
    public string ComputerName { get; set; } = "LAPTOP-7";

    public WindowsVersion Windows = new("Windows 10 Pro", "Professional", "24H2", 26100, 2033);
    public ProcessIdentity Process = new(@"LAPTOP-7\alex", true);
    public LaunchOrigin Launch = new(new ParentProcess(4120, "explorer"), 1388);

    public Exception? WindowsFailure { get; set; }
    public Exception? ProcessFailure { get; set; }
    public Exception? LaunchFailure { get; set; }

    public WindowsVersion ReadWindows() => WindowsFailure != null ? throw WindowsFailure : Windows;

    public ProcessIdentity ReadProcess() => ProcessFailure != null ? throw ProcessFailure : Process;

    public LaunchOrigin ReadLaunch() => LaunchFailure != null ? throw LaunchFailure : Launch;
}

internal sealed class FakeClock : IClock
{
    public DateTime Now { get; set; } = new(2026, 10, 23, 9, 15, 42, 317);
}
