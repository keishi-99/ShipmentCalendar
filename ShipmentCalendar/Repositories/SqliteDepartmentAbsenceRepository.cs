using Microsoft.Data.Sqlite;
using ShipmentCalendar.Data;
using ShipmentCalendar.Models;

namespace ShipmentCalendar.Repositories;

/// <summary>部署別・日別欠員数のSQLiteリポジトリ</summary>
public static class SqliteDepartmentAbsenceRepository
{
    public static async Task<IEnumerable<DepartmentAbsence>> GetAllAsync()
    {
        List<DepartmentAbsence> list = [];
        using var connection = new SqliteConnection(DepartmentDatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, DepartmentId, Date, AbsentCount FROM DepartmentAbsences";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(ReadAbsence(reader));

        return list;
    }

    public static async Task<IEnumerable<DepartmentAbsence>> GetByMonthAsync(int year, int month)
    {
        List<DepartmentAbsence> list = [];
        using var connection = new SqliteConnection(DepartmentDatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, DepartmentId, Date, AbsentCount FROM DepartmentAbsences WHERE Date LIKE $yearMonth";
        command.Parameters.AddWithValue("$yearMonth", $"{year:D4}-{month:D2}%");
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(ReadAbsence(reader));

        return list;
    }

    /// <summary>指定日の欠員数を更新する。0以下の場合は行自体を削除する（欠員なしがデフォルト状態のため）</summary>
    public static async Task UpsertAsync(int departmentId, DateOnly date, int absentCount)
    {
        using var connection = new SqliteConnection(DepartmentDatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        if (absentCount <= 0)
        {
            command.CommandText = "DELETE FROM DepartmentAbsences WHERE DepartmentId = $deptId AND Date = $date";
        }
        else
        {
            command.CommandText = @"
                INSERT INTO DepartmentAbsences (DepartmentId, Date, AbsentCount) VALUES ($deptId, $date, $count)
                ON CONFLICT(DepartmentId, Date) DO UPDATE SET AbsentCount = excluded.AbsentCount";
            command.Parameters.AddWithValue("$count", absentCount);
        }
        command.Parameters.AddWithValue("$deptId", departmentId);
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        await command.ExecuteNonQueryAsync();
    }

    private static DepartmentAbsence ReadAbsence(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        DepartmentId = reader.GetInt32(1),
        Date = DateOnly.Parse(reader.GetString(2)),
        AbsentCount = reader.GetInt32(3)
    };
}
