using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public interface IHolidayRepository
{
    Task<IEnumerable<Holiday>> GetAllAsync();
    Task<IEnumerable<Holiday>> GetByYearAsync(int year);
    Task AddAsync(Holiday holiday);
    Task DeleteAsync(int id);
}
