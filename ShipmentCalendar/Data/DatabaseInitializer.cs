using Microsoft.Data.Sqlite;
using ShipmentCalendar.Services;
using System.IO;

namespace ShipmentCalendar.Data;

/// <summary>SQLiteデータベース初期化・接続管理（工程マスタ・休日のみ管理）</summary>
public static class DatabaseInitializer {
    private static readonly string _dataDir = AppSettingsService.GetSharedDataDir();

    private static readonly string _dbPath = Path.Combine(_dataDir, "shipment.db");

    public static string ConnectionString => $"Data Source={_dbPath}";

    public static void Initialize() {
        Directory.CreateDirectory(_dataDir);

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

            CREATE TABLE IF NOT EXISTS Holidays (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS Departments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                Headcount INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS DepartmentAbsences (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DepartmentId INTEGER NOT NULL,
                Date TEXT NOT NULL,
                AbsentCount INTEGER NOT NULL DEFAULT 0,
                UNIQUE(DepartmentId, Date)
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

        // CREATE TABLE IF NOT EXISTSは既存テーブルには列を追加しないため、既存DBへの列追加はALTER TABLEで個別に行う
        MigrateAddColumnIfMissing(connection, "Departments", "Headcount", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void MigrateAddColumnIfMissing(SqliteConnection connection, string table, string column, string columnDefinition) {
        bool exists;
        using (var checkCommand = connection.CreateCommand()) {
            checkCommand.CommandText = $"PRAGMA table_info({table})";
            using var reader = checkCommand.ExecuteReader();
            exists = false;
            while (reader.Read()) {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) {
                    exists = true;
                    break;
                }
            }
        }
        if (exists) return;

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDefinition}";
        try {
            alterCommand.ExecuteNonQuery();
        } catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)) {
            // 共有DBに対して複数PCがほぼ同時に起動した場合、他PCが先にこの列を追加済みであることがある。
            // 上のexistsチェックと実際のALTER TABLEの間には排他がないため、ここで発生しうる
        }
    }
}
