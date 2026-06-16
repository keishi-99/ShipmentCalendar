using Microsoft.Data.Sqlite;
using System.IO;

namespace ShipmentCalendar.Data;

/// <summary>SQLiteデータベース初期化・接続管理（工程マスタ・休日のみ管理）</summary>
public static class DatabaseInitializer
{
    private static readonly string DataDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "data");

    private static string _dbPath = Path.Combine(DataDir, "shipment.db");

    public static string ConnectionString => $"Data Source={_dbPath}";

    public static void Initialize()
    {
        Directory.CreateDirectory(DataDir);

        // 旧パス（exeと同じ場所）から新パス（data/）へ移行する
        var oldPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shipment.db");
        if (File.Exists(oldPath) && !File.Exists(_dbPath))
            File.Move(oldPath, _dbPath);

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProductName TEXT NOT NULL UNIQUE,
                ItemNumber TEXT NOT NULL DEFAULT ''
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
                CoolTimeMinutes REAL NOT NULL DEFAULT 0,
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
                SortOrder INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS ModelCodeDefinitions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ModelCode TEXT NOT NULL UNIQUE,
                Name TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0
            );

            DROP TABLE IF EXISTS FinishedProducts;
        ";
        command.ExecuteNonQuery();

        // デフォルト部署を登録（既存なら無視）
        using var insertDepts = connection.CreateCommand();
        insertDepts.CommandText = @"
            INSERT OR IGNORE INTO Departments (Name, SortOrder) VALUES ('製造課', 0);
            INSERT OR IGNORE INTO Departments (Name, SortOrder) VALUES ('検査課', 1);
        ";
        insertDepts.ExecuteNonQuery();

        MigrateAddColumnIfNotExists(connection, "ProcessDefinitions", "SetupTimeMinutes", "REAL NOT NULL DEFAULT 0");
        MigrateSplitLeadTimeMinutes(connection);
        MigrateAddColumnIfNotExists(connection, "ProcessDefinitions", "IsVisible", "INTEGER NOT NULL DEFAULT 1");
        MigrateRenameOrAddColumn(connection, "ProcessDefinitions", "CsvColumnName", "DestinationCode", "TEXT NOT NULL DEFAULT ''");
        MigrateAddColumnIfNotExists(connection, "ProcessDefinitions", "WarningDaysBeforeDeadline", "INTEGER NOT NULL DEFAULT 0");
        MigrateAddColumnIfNotExists(connection, "ProcessDefinitions", "DepartmentId", "INTEGER NOT NULL DEFAULT 0");
        MigrateAddColumnIfNotExists(connection, "ProcessDefinitions", "CoolTimeMinutes", "REAL NOT NULL DEFAULT 0");
        MigrateAddColumnIfNotExists(connection, "ProcessDefinitions", "OutsourceLeadDays", "INTEGER NOT NULL DEFAULT 0");

        // 既存DBのProcessDefinitions.ItemNumberをProductsテーブルに移行
        MigrateAddColumnIfNotExists(connection, "Products", "DisplayName", "TEXT NOT NULL DEFAULT ''");
        MigrateProductsFromProcessDefinitions(connection);
    }

    /// <summary>ProcessDefinitions.ItemNumber が存在する場合、Productsテーブルへ移行する</summary>
    private static void MigrateProductsFromProcessDefinitions(SqliteConnection connection)
    {
        // ItemNumber列が存在しない場合はスキップ（既に移行済み or 新規DB）
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(ProcessDefinitions)";
        bool hasItemNumber = false;
        using (var reader = check.ExecuteReader())
        {
            while (reader.Read())
            {
                if (reader.GetString(1) == "ItemNumber") { hasItemNumber = true; break; }
            }
        }
        if (!hasItemNumber) return;

        // 既存のProductNameとItemNumberをProductsに移行（重複はスキップ）
        using var migrate = connection.CreateCommand();
        migrate.CommandText = @"
            INSERT OR IGNORE INTO Products (ProductName, ItemNumber)
            SELECT DISTINCT ItemNumber, COALESCE(ItemNumber, '') FROM ProcessDefinitions
            WHERE ItemNumber != ''";
        migrate.ExecuteNonQuery();
    }

    /// <summary>旧LeadTimeMinutes列（段取時間+作業時間の合計）をWorkTimeMinutes列に引き継いでから削除する</summary>
    private static void MigrateSplitLeadTimeMinutes(SqliteConnection connection)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(ProcessDefinitions)";
        bool hasLeadTime = false, hasWorkTime = false;
        using (var reader = check.ExecuteReader())
        {
            while (reader.Read())
            {
                var col = reader.GetString(1);
                if (col == "LeadTimeMinutes") hasLeadTime = true;
                if (col == "WorkTimeMinutes") hasWorkTime = true;
            }
        }

        if (!hasWorkTime)
        {
            using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE ProcessDefinitions ADD COLUMN WorkTimeMinutes REAL NOT NULL DEFAULT 0";
            add.ExecuteNonQuery();

            // 既存の標準時間（段取+作業の合計）を作業時間に引き継ぎ、合計値（計算結果）を変えない
            if (hasLeadTime)
            {
                using var copy = connection.CreateCommand();
                copy.CommandText = "UPDATE ProcessDefinitions SET WorkTimeMinutes = COALESCE(LeadTimeMinutes, 0)";
                copy.ExecuteNonQuery();
            }
        }

        if (hasLeadTime)
        {
            using var drop = connection.CreateCommand();
            drop.CommandText = "ALTER TABLE ProcessDefinitions DROP COLUMN LeadTimeMinutes";
            drop.ExecuteNonQuery();
        }
    }

    private static void MigrateAddColumnIfNotExists(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == column) return;
        }
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    /// <summary>旧列名が存在すれば新列名にリネームし、どちらも存在しなければ新列名で追加する</summary>
    private static void MigrateRenameOrAddColumn(SqliteConnection connection, string table, string oldColumn, string newColumn, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        bool hasOld = false, hasNew = false;
        using (var reader = check.ExecuteReader())
        {
            while (reader.Read())
            {
                var col = reader.GetString(1);
                if (col == oldColumn) hasOld = true;
                if (col == newColumn) hasNew = true;
            }
        }
        if (hasNew) return;

        using var alter = connection.CreateCommand();
        alter.CommandText = hasOld
            ? $"ALTER TABLE {table} RENAME COLUMN {oldColumn} TO {newColumn}"
            : $"ALTER TABLE {table} ADD COLUMN {newColumn} {definition}";
        alter.ExecuteNonQuery();
    }
}
