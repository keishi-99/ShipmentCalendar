using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

/// <summary>MainViewModelの受注ロード処理がテストでモック化するための、OdbcProcessDefinitionRepositoryの最小インターフェース</summary>
public interface IOdbcProcessDefinitionRepository {
    IEnumerable<ProcessDefinition> GetAll();
}
