namespace WiFiControl.Core.Model;

/// <summary>
/// Сетевые настройки компьютера. Единственное место, через которое ядро обращается к системе;
/// в тестах подменяется. Методы при неудаче бросают исключение с объяснением, понятным человеку.
/// Перенесено из NetSwitch.
/// </summary>
public interface INetworkSystem
{
    /// <summary>Все адаптеры, видимые в «Сетевых подключениях», физические и виртуальные.</summary>
    Task<IReadOnlyList<Adapter>> ReadAdaptersAsync(CancellationToken cancellationToken);
}
