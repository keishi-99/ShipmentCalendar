using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

/// <summary>MainViewModelの受注ロード処理がテストでモック化するための、OdbcOrderRepositoryの最小インターフェース</summary>
public interface IOdbcOrderRepository {
    IEnumerable<Order> GetAll();
    /// <summary>出荷日が指定範囲内の受注を取得する。基本設定の取得範囲（過去日数・表示範囲日数）に関わらず任意の期間を指定できる</summary>
    IEnumerable<Order> GetByDeliveryDateRange(DateOnly from, DateOnly to);
    bool HasAnySeisanKeikakuRecord();
}
