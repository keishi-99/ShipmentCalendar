using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public interface IFinishedProductRepository
{
    Task<IEnumerable<FinishedProductDefinition>> GetAllAsync();
    Task AddAsync(FinishedProductDefinition definition);
    Task UpdateAsync(FinishedProductDefinition definition);
    Task DeleteAsync(int id);
}
