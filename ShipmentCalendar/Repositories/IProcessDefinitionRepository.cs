using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public interface IProcessDefinitionRepository
{
    Task<IEnumerable<ProcessDefinition>> GetAllAsync();
    Task<IEnumerable<ProcessDefinition>> GetByItemNumberAsync(string itemNumber);
    Task<IEnumerable<string>> GetItemNumbersAsync();
    Task AddAsync(ProcessDefinition definition);
    Task UpdateAsync(ProcessDefinition definition);
    Task DeleteAsync(int id);
    /// <summary>指定品目番号の工程定義を、1トランザクション内で全削除→全追加に置き換える</summary>
    Task ReplaceForItemNumberAsync(string itemNumber, IEnumerable<ProcessDefinition> definitions);
}
