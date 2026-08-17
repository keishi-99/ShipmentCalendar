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
        using var connection = new SqliteConnection(DepartmentDatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, SortOrder, Headcount FROM Departments ORDER BY SortOrder, Id";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new Department
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                SortOrder = reader.GetInt32(2),
                Headcount = reader.GetInt32(3)
            });

        return list;
    }

    /// <summary>部署の基本人数を更新する</summary>
    public static async Task UpdateHeadcountAsync(int id, int headcount)
    {
        using var connection = new SqliteConnection(DepartmentDatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE Departments SET Headcount = $headcount WHERE Id = $id";
        command.Parameters.AddWithValue("$headcount", headcount);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>部署の表示順を更新する</summary>
    public static async Task UpdateSortOrderAsync(int id, int sortOrder)
    {
        using var connection = new SqliteConnection(DepartmentDatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE Departments SET SortOrder = $sortOrder WHERE Id = $id";
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>部署を追加する。名前が既存と重複していてINSERTされなかった場合はfalseを返す</summary>
    public static async Task<bool> AddAsync(string name)
    {
        using var connection = new SqliteConnection(DepartmentDatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO Departments (Name, SortOrder) VALUES ($name, (SELECT COALESCE(MAX(SortOrder)+1, 0) FROM Departments))";
        command.Parameters.AddWithValue("$name", name);
        var affected = await command.ExecuteNonQueryAsync();
        return affected > 0;
    }

    public static async Task DeleteAsync(int id)
    {
        // ProcessDefinitionsはprocess.db（別ファイル）にあるため、department.dbのトランザクションには含められない。
        // 先に工程側の担当部署を未設定へ更新してから部署を削除することで、
        // 万一この後の削除に失敗しても「存在しない部署IDを参照したままの工程」が残らないようにする
        using (var processConnection = new SqliteConnection(ProcessDatabaseInitializer.ConnectionString))
        {
            await processConnection.OpenAsync();
            var updateCmd = processConnection.CreateCommand();
            updateCmd.CommandText = "UPDATE ProcessDefinitions SET DepartmentId = 0 WHERE DepartmentId = $id";
            updateCmd.Parameters.AddWithValue("$id", id);
            await updateCmd.ExecuteNonQueryAsync();
        }

        using var connection = new SqliteConnection(DepartmentDatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var deleteCmd = connection.CreateCommand();
            deleteCmd.Transaction = transaction as SqliteTransaction;
            deleteCmd.CommandText = "DELETE FROM Departments WHERE Id = $id";
            deleteCmd.Parameters.AddWithValue("$id", id);
            await deleteCmd.ExecuteNonQueryAsync();

            var deleteAbsencesCmd = connection.CreateCommand();
            deleteAbsencesCmd.Transaction = transaction as SqliteTransaction;
            deleteAbsencesCmd.CommandText = "DELETE FROM DepartmentAbsences WHERE DepartmentId = $id";
            deleteAbsencesCmd.Parameters.AddWithValue("$id", id);
            await deleteAbsencesCmd.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
