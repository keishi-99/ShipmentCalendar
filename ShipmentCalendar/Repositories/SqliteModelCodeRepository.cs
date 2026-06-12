using Microsoft.Data.Sqlite;
using ShipmentCalendar.Data;
using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

public class SqliteModelCodeRepository : IModelCodeRepository
{
    public async Task<IEnumerable<ModelCodeDefinition>> GetAllAsync()
    {
        var definitions = new List<ModelCodeDefinition>();
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ModelCode, Name, Category, SortOrder FROM ModelCodeDefinitions ORDER BY SortOrder";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            definitions.Add(ReadDefinition(reader));

        return definitions;
    }

    public async Task AddAsync(ModelCodeDefinition definition)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ModelCodeDefinitions (ModelCode, Name, Category, SortOrder)
            VALUES ($modelCode, $name, $category, $so)";
        command.Parameters.AddWithValue("$modelCode", definition.ModelCode);
        command.Parameters.AddWithValue("$name", definition.Name);
        command.Parameters.AddWithValue("$category", definition.Category);
        command.Parameters.AddWithValue("$so", definition.SortOrder);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(ModelCodeDefinition definition)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE ModelCodeDefinitions SET ModelCode=$modelCode, Name=$name, Category=$category, SortOrder=$so
            WHERE Id=$id";
        command.Parameters.AddWithValue("$modelCode", definition.ModelCode);
        command.Parameters.AddWithValue("$name", definition.Name);
        command.Parameters.AddWithValue("$category", definition.Category);
        command.Parameters.AddWithValue("$so", definition.SortOrder);
        command.Parameters.AddWithValue("$id", definition.Id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ModelCodeDefinitions WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task ReplaceAllAsync(IEnumerable<ModelCodeDefinition> definitions)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM ModelCodeDefinitions";
            await deleteCommand.ExecuteNonQueryAsync();
        }

        foreach (var definition in definitions)
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = @"
                INSERT INTO ModelCodeDefinitions (ModelCode, Name, Category, SortOrder)
                VALUES ($modelCode, $name, $category, $so)";
            insertCommand.Parameters.AddWithValue("$modelCode", definition.ModelCode);
            insertCommand.Parameters.AddWithValue("$name", definition.Name);
            insertCommand.Parameters.AddWithValue("$category", definition.Category);
            insertCommand.Parameters.AddWithValue("$so", definition.SortOrder);
            await insertCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static ModelCodeDefinition ReadDefinition(SqliteDataReader reader) => new ModelCodeDefinition
    {
        Id = reader.GetInt32(0),
        ModelCode = reader.GetString(1),
        Name = reader.GetString(2),
        Category = reader.GetString(3),
        SortOrder = reader.GetInt32(4)
    };
}
