using Microsoft.Data.Sqlite;
using ShipmentCalendar.Services;
using System.IO;

namespace ShipmentCalendar.Data;

/// <summary>SQLiteデータベース初期化・接続管理（工程マスタ・休日のみ管理）。
/// 既存テーブルへの列追加マイグレーションは行わない（複数PC同時起動時の競合を避けるため）。
/// そのため、このバージョンより前に作成された共有DBを引き続き使う場合は、
/// 管理者が事前に手動でスキーマを最新化しておく必要がある
/// （例: ALTER TABLE Departments ADD COLUMN Headcount INTEGER NOT NULL DEFAULT 0;）。
/// 新規に作成する共有DBはCREATE TABLE IF NOT EXISTSの定義がそのまま最新スキーマになるため対応不要</summary>
public static class DatabaseInitializer {
    private static readonly string? _dataDir = AppSettingsService.GetSharedDataDir();

    private static readonly string? _dbPath = _dataDir != null ? Path.Combine(_dataDir, "shipment.db") : null;

    /// <summary>共有データフォルダが設定されているか。falseの場合、マスタDBを扱う機能は使用できない</summary>
    public static bool IsAvailable => _dbPath != null;

    public static string ConnectionString => _dbPath != null
        ? new SqliteConnectionStringBuilder { DataSource = _dbPath, DefaultTimeout = 5 }.ToString()
        : throw new InvalidOperationException("共有データフォルダが設定されていません。設定 > 基本設定 から設定してください。");

    public static void Initialize() {
        if (_dataDir == null) return;

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
    }
}
