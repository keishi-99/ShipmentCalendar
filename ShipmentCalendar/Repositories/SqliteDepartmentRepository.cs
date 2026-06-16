using Microsoft.Data.Sqlite;
using ShipmentCalendar.Data;
using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

/// <summary>担当部署マスタのSQLiteリポジトリ</summary>
public static class SqliteDepartmentRepository
{
    public static async Task<IEnumerable<Department>> GetAllAsync()
    {
        List<Department> list = [];
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

    /// <summary>部署を追加する。名前が既存と重複していてINSERTされなかった場合はfalseを返す</summary>
    public static async Task<bool> AddAsync(string name)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO Departments (Name, SortOrder) VALUES ($name, (SELECT COALESCE(MAX(SortOrder)+1, 0) FROM Departments))";
        command.Parameters.AddWithValue("$name", name);
        var affected = await command.ExecuteNonQueryAsync();
        return affected > 0;
    }

    public static async Task DeleteAsync(int id)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        // 部署削除と同時にProcessDefinitionsの該当DepartmentIdを0（未設定）に更新
        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var deleteCmd = connection.CreateCommand();
            deleteCmd.Transaction = transaction as SqliteTransaction;
            deleteCmd.CommandText = "DELETE FROM Departments WHERE Id = $id";
            deleteCmd.Parameters.AddWithValue("$id", id);
            await deleteCmd.ExecuteNonQueryAsync();

            var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction as SqliteTransaction;
            updateCmd.CommandText = "UPDATE ProcessDefinitions SET DepartmentId = 0 WHERE DepartmentId = $id";
            updateCmd.Parameters.AddWithValue("$id", id);
            await updateCmd.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
