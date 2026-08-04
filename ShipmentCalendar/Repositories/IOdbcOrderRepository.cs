using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

/// <summary>MainViewModelの受注ロード処理がテストでモック化するための、OdbcOrderRepositoryの最小インターフェース</summary>
public interface IOdbcOrderRepository {
    IEnumerable<Order> GetAll();
    bool HasAnySeisanKeikakuRecord();
}
