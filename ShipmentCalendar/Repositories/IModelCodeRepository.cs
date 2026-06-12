using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public interface IModelCodeRepository
{
    Task<IEnumerable<ModelCodeDefinition>> GetAllAsync();
    Task AddAsync(ModelCodeDefinition definition);
    Task UpdateAsync(ModelCodeDefinition definition);
    Task DeleteAsync(int id);
}
