using Microsoft.Data.Sqlite;
using ShipmentCalendar.Data;
using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

/// <summary>担当部署マスタのSQLiteリポジトリ</summary>
public class SqliteDepartmentRepository
{
    public async Task<IEnumerable<Department>> GetAllAsync()
    {
        var list = new List<Department>();
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, SortOrder FROM Departments ORDER BY SortOrder, Id";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new Department
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                SortOrder = reader.GetInt32(2)
            });

        return list;
    }

    public async Task AddAsync(string name)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO Departments (Name, SortOrder) VALUES ($name, (SELECT COALESCE(MAX(SortOrder)+1, 0) FROM Departments))";
        command.Parameters.AddWithValue("$name", name);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Departments WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }
}
