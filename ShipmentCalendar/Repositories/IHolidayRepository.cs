using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public interface IHolidayRepository
{
    Task<IEnumerable<Holiday>> GetAllAsync();
    Task<IEnumerable<Holiday>> GetByYearAsync(int year);
    /// <summary>指定年の休日を、指定した日付一覧で丸ごと置き換える</summary>
    Task ReplaceYearAsync(int year, IEnumerable<DateOnly> dates);
}
