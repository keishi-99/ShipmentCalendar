using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

/// <summary>注文データ取得の抽象インターフェース（CSV/ODBC切替ポイント）</summary>
public interface IOrderRepository
{
    Task<IEnumerable<Order>> GetAllAsync();
}
