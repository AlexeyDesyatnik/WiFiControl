namespace WiFiControl.Core.Model;

/// <summary>Последнее применение: сеть и профиль, применённые последними.</summary>
/// <param name="Network">Ключ сети (<see cref="NetworkKey"/>).</param>
/// <param name="ProfileId"><c>id</c> применённого профиля.</param>
public sealed record LastApplication(string Network, Guid ProfileId)
{
    /// <summary>Wi-Fi-адаптер последнего применения, если он известен.</summary>
    public Guid? AdapterId { get; init; }
}
