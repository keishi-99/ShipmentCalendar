using Microsoft.Data.Sqlite;
using ShipmentCalendar.Data;
using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public class SqliteFinishedProductRepository : IFinishedProductRepository
{
    public async Task<IEnumerable<FinishedProductDefinition>> GetAllAsync()
    {
        var products = new List<FinishedProductDefinition>();
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, ItemNumberPrefix, SortOrder FROM FinishedProducts ORDER BY SortOrder";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            products.Add(ReadDefinition(reader));

        return products;
    }

    public async Task AddAsync(FinishedProductDefinition definition)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO FinishedProducts (Name, ItemNumberPrefix, SortOrder)
            VALUES ($name, $prefix, $so)";
        command.Parameters.AddWithValue("$name", definition.Name);
        command.Parameters.AddWithValue("$prefix", definition.ItemNumberPrefix);
        command.Parameters.AddWithValue("$so", definition.SortOrder);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(FinishedProductDefinition definition)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE FinishedProducts SET Name=$name, ItemNumberPrefix=$prefix, SortOrder=$so
            WHERE Id=$id";
        command.Parameters.AddWithValue("$name", definition.Name);
        command.Parameters.AddWithValue("$prefix", definition.ItemNumberPrefix);
        command.Parameters.AddWithValue("$so", definition.SortOrder);
        command.Parameters.AddWithValue("$id", definition.Id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FinishedProducts WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static FinishedProductDefinition ReadDefinition(SqliteDataReader reader) => new FinishedProductDefinition
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
        ItemNumberPrefix = reader.GetString(2),
        SortOrder = reader.GetInt32(3)
    };
}
