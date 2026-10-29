namespace WiFiControl.Core.Model;

/// <summary>Последнее применение: сеть и профиль, применённые последними.</summary>
/// <param name="Network">Ключ сети (<see cref="NetworkKey"/>).</param>
/// <param name="Profile">Профиль в том виде, в каком его применили.</param>
public sealed record LastApplication(string Network, Profile Profile);
