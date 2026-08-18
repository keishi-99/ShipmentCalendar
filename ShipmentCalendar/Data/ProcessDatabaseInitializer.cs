using Microsoft.Data.Sqlite;

namespace ShipmentCalendar.Data;

/// <summary>工程マスタDB（process.db）の初期化・接続管理（製品・工程・機種コードを管理）。
/// 既存テーブルへの列追加マイグレーションは行わない（複数PC同時起動時の競合を避けるため）。
/// そのため、このバージョンより前に作成された共有DBを引き続き使う場合は、
/// 管理者が事前に手動でスキーマを最新化しておく必要がある
/// （例: ALTER TABLE ProcessDefinitions ADD COLUMN OutsourceLeadDays INTEGER NOT NULL DEFAULT 0;）。
/// 新規に作成する共有DBはCREATE TABLE IF NOT EXISTSの定義がそのまま最新スキーマになるため対応不要</summary>
public static class ProcessDatabaseInitializer {
    private const string FileName = "process.db";

    public static string ConnectionString => SharedDatabase.ConnectionStringFor(FileName);

    public static void Initialize() {
        if (!SharedDatabase.IsAvailable) return;

        SharedDatabase.EnsureDataDirExists();

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ItemNumber TEXT NOT NULL UNIQUE,
                DisplayName TEXT NOT NULL DEFAULT '',
                CompletionDateLeadDays INTEGER NULL
            );

            CREATE TABLE IF NOT EXISTS ProcessDefinitions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ItemNumber TEXT NOT NULL,
                ProcessName TEXT NOT NULL,
                SetupTimeMinutes REAL NOT NULL DEFAULT 0,
                WorkTimeMinutes REAL NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsVisible INTEGER NOT NULL DEFAULT 1,
                DestinationCode TEXT NOT NULL DEFAULT '',
                WarningDaysBeforeDeadline INTEGER NOT NULL DEFAULT 0,
                DepartmentId INTEGER NOT NULL DEFAULT 0,
                DwellTimeMinutes REAL NOT NULL DEFAULT 0,
                OutsourceLeadDays INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS ModelCodeDefinitions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ModelCode TEXT NOT NULL UNIQUE,
                Name TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0
            );
        ";
        command.ExecuteNonQuery();
    }
}
