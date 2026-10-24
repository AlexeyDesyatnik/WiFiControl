namespace WiFiControl.Recon.Model;

/// <summary>Интерфейс IPv4 сетевого стека (<c>MIB_IPINTERFACE_ROW</c>): у каждого включённого адаптера с IPv4 — свой.</summary>
/// <param name="AdapterId">GUID адаптера, тот же, что в «Сетевых подключениях».</param>
/// <param name="Index">Номер интерфейса, по которому на него ссылаются маршруты.</param>
/// <param name="Metric">Метрика интерфейса; Windows прибавляет её к метрике маршрута.</param>
/// <param name="AutomaticMetric">Метрику назначает Windows по скорости соединения, а не человек.</param>
internal sealed record IpInterface(Guid AdapterId, int Index, int Metric, bool AutomaticMetric);

/// <summary>Маршрут IPv4 из таблицы маршрутов (<c>MIB_IPFORWARD_ROW2</c>).</summary>
/// <param name="Destination">Сеть назначения с длиной префикса: 0.0.0.0/0 у маршрута по умолчанию.</param>
/// <param name="Metric">Метрика самого маршрута, без метрики интерфейса.</param>
internal sealed record Route(Guid AdapterId, int InterfaceIndex, string Destination, string NextHop, int Metric);

/// <summary>Маршрут, который Windows выбрала для адреса (<c>GetBestRoute2</c>), и адрес, с которого ушёл бы трафик.</summary>
internal sealed record BestRoute(Route Route, string SourceAddress);
