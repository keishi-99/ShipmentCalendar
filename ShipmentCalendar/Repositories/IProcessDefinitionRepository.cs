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
}
