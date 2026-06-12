using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public interface IModelCodeRepository
{
    Task<IEnumerable<ModelCodeDefinition>> GetAllAsync();
    Task AddAsync(ModelCodeDefinition definition);
    Task UpdateAsync(ModelCodeDefinition definition);
    Task DeleteAsync(int id);
    /// <summary>機種コード定義を、1トランザクション内で全削除→全追加に置き換える</summary>
    Task ReplaceAllAsync(IEnumerable<ModelCodeDefinition> definitions);
}
