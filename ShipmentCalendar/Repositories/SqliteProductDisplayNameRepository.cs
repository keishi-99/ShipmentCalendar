using Microsoft.Data.Sqlite;
using ShipmentCalendar.Data;

namespace ShipmentCalendar.Repositories;

/// <summary>品目番号ごとの表示用品目名をProductsテーブルで管理する</summary>
public static class SqliteProductDisplayNameRepository
{
    /// <summary>品目番号に対応する表示名を取得する（未登録時はnull）</summary>
    public static async Task<string?> GetDisplayNameAsync(string itemNumber)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DisplayName FROM Products WHERE ItemNumber = $pn LIMIT 1";
        command.Parameters.AddWithValue("$pn", itemNumber);
        var result = await command.ExecuteScalarAsync();
        return result is string s ? s : null;
    }

    /// <summary>全品目の品目番号→表示名辞書を返す</summary>
    public static async Task<Dictionary<string, string>> GetAllDisplayNamesAsync()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ItemNumber, DisplayName FROM Products WHERE ItemNumber != '' AND DisplayName != ''";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            dict[reader.GetString(0)] = reader.GetString(1);

        return dict;
    }

    /// <summary>品目番号に対応する表示名を保存する（INSERT OR REPLACE）</summary>
    public static async Task SaveDisplayNameAsync(string itemNumber, string displayName)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Products (ItemNumber, DisplayName)
            VALUES ($pn, $dn)
            ON CONFLICT(ItemNumber) DO UPDATE SET DisplayName = excluded.DisplayName";
        command.Parameters.AddWithValue("$pn", itemNumber);
        command.Parameters.AddWithValue("$dn", displayName);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>品目番号に対応する完了日リードタイム（営業日数）を取得する（未設定時はnull）</summary>
    public static async Task<int?> GetCompletionDateLeadDaysAsync(string itemNumber)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CompletionDateLeadDays FROM Products WHERE ItemNumber = $pn LIMIT 1";
        command.Parameters.AddWithValue("$pn", itemNumber);
        var result = await command.ExecuteScalarAsync();
        return result is long l ? (int)l : null;
    }

    /// <summary>全品目の品目番号→完了日リードタイム辞書を返す（未設定の品目は含まれない）</summary>
    public static async Task<Dictionary<string, int>> GetAllCompletionDateLeadDaysAsync()
    {
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ItemNumber, CompletionDateLeadDays FROM Products WHERE ItemNumber != '' AND CompletionDateLeadDays IS NOT NULL";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            dict[reader.GetString(0)] = reader.GetInt32(1);

        return dict;
    }

    /// <summary>品目番号に対応する完了日リードタイムを保存する（null指定で未設定=グローバル値使用に戻す）</summary>
    public static async Task SaveCompletionDateLeadDaysAsync(string itemNumber, int? leadDays)
    {
        using var connection = new SqliteConnection(DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Products (ItemNumber, CompletionDateLeadDays)
            VALUES ($pn, $ld)
            ON CONFLICT(ItemNumber) DO UPDATE SET CompletionDateLeadDays = excluded.CompletionDateLeadDays";
        command.Parameters.AddWithValue("$pn", itemNumber);
        command.Parameters.AddWithValue("$ld", (object?)leadDays ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
}
