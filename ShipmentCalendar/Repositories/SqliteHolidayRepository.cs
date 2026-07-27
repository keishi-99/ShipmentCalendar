using Microsoft.Data.Sqlite;
using ShipmentCalendar.Data;
using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public class SqliteHolidayRepository : IHolidayRepository
{
    public async Task<IEnumerable<Holiday>> GetAllAsync()
    {
        List<Holiday> holidays = [];
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
        List<Holiday> holidays = [];
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

    public async Task ReplaceYearAsync(int year, IEnumerable<DateOnly> dates)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM Holidays WHERE Date LIKE $year";
        deleteCommand.Parameters.AddWithValue("$year", $"{year}%");
        await deleteCommand.ExecuteNonQueryAsync();

        foreach (var date in dates)
        {
            var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO Holidays (Date, Description) VALUES ($date, '')";
            insertCommand.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
            await insertCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static Holiday ReadHoliday(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Date = DateOnly.Parse(reader.GetString(1)),
        Description = reader.GetString(2)
    };
}
