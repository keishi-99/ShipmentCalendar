using Microsoft.Data.Sqlite;
using ShipmentCalendar.Data;
using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public class SqliteHolidayRepository : IHolidayRepository
{
    public async Task<IEnumerable<Holiday>> GetAllAsync()
    {
        var holidays = new List<Holiday>();
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Date, Description FROM Holidays ORDER BY Date";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            holidays.Add(ReadHoliday(reader));

        return holidays;
    }

    public async Task<IEnumerable<Holiday>> GetByYearAsync(int year)
    {
        var holidays = new List<Holiday>();
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Date, Description FROM Holidays WHERE Date LIKE $year ORDER BY Date";
        command.Parameters.AddWithValue("$year", $"{year}%");
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            holidays.Add(ReadHoliday(reader));

        return holidays;
    }

    public async Task AddAsync(Holiday holiday)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO Holidays (Date, Description) VALUES ($date, $desc)";
        command.Parameters.AddWithValue("$date", holiday.Date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$desc", holiday.Description);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Holidays WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static Holiday ReadHoliday(SqliteDataReader reader) => new Holiday
    {
        Id = reader.GetInt32(0),
        Date = DateOnly.Parse(reader.GetString(1)),
        Description = reader.GetString(2)
    };
}
