using Microsoft.Data.Sqlite;

namespace ShipmentCalendar.Data;

/// <summary>休日マスタDB（holiday.db）の初期化・接続管理。
/// 既存テーブルへの列追加マイグレーションは行わない（複数PC同時起動時の競合を避けるため）。
/// そのため、このバージョンより前に作成された共有DBを引き続き使う場合は、
/// 管理者が事前に手動でスキーマを最新化しておく必要がある。
/// 新規に作成する共有DBはCREATE TABLE IF NOT EXISTSの定義がそのまま最新スキーマになるため対応不要</summary>
public static class HolidayDatabaseInitializer {
    private const string FileName = "holiday.db";

    public static string ConnectionString => SharedDatabase.ConnectionStringFor(FileName);

    public static void Initialize() {
        if (!SharedDatabase.IsAvailable) return;

        SharedDatabase.EnsureDataDirExists();

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Holidays (
                Date TEXT PRIMARY KEY,
                Description TEXT NOT NULL DEFAULT ''
            );
        ";
        command.ExecuteNonQuery();
    }
}
