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

        var command = connection.CreateCommand();
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
                LeadTimeMinutes REAL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsVisible INTEGER NOT NULL DEFAULT 1,
                CsvColumnName TEXT NOT NULL DEFAULT '',
                WarningDaysBeforeDeadline INTEGER NOT NULL DEFAULT 0
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
        ";
        command.ExecuteNonQuery();

        // デフォルト部署を登録（既存なら無視）
        var insertDepts = connection.CreateCommand();
        insertDepts.CommandText = @"
            INSERT OR IGNORE INTO Departments (Name, SortOrder) VALUES ('製造課', 0);
            INSERT OR IGNORE INTO Departments (Name, SortOrder) VALUES ('検査課', 1);
        ";
        insertDepts.ExecuteNonQuery();

        MigrateAddColumnIfNotExists(connection, "ProcessDefinitions", "LeadTimeMinutes", "REAL NOT NULL DEFAULT 0");
        // 既存DBのLeadTimeHours（時間単位）→LeadTimeMinutes（分単位）×60で移行
        // 既存DBのLeadTimeDays（日単位）→LeadTimeMinutes（分単位）×480で移行
        var checkColCmd = connection.CreateCommand();
        checkColCmd.CommandText = "PRAGMA table_info(ProcessDefinitions)";
        bool hasLeadTimeHours = false, hasLeadTimeDays = false;
        using (var r = checkColCmd.ExecuteReader())
        {
            while (r.Read())
            {
                var col = r.GetString(1);
                if (col == "LeadTimeHours") hasLeadTimeHours = true;
                if (col == "LeadTimeDays") hasLeadTimeDays = true;
            }
        }
        if (hasLeadTimeHours)
        {
            var migrateCmd = connection.CreateCommand();
            migrateCmd.CommandText = "UPDATE ProcessDefinitions SET LeadTimeMinutes = LeadTimeHours * 60.0 WHERE LeadTimeMinutes = 0 AND LeadTimeHours > 0";
            migrateCmd.ExecuteNonQuery();
        }
        else if (hasLeadTimeDays)
        {
            var migrateCmd = connection.CreateCommand();
            migrateCmd.CommandText = "UPDATE ProcessDefinitions SET LeadTimeMinutes = CAST(LeadTimeDays AS REAL) * 480.0 WHERE LeadTimeMinutes = 0";
            migrateCmd.ExecuteNonQuery();
        }
        MigrateAddColumnIfNotExists(connection, "ProcessDefinitions", "IsVisible", "INTEGER NOT NULL DEFAULT 1");
        MigrateAddColumnIfNotExists(connection, "ProcessDefinitions", "CsvColumnName", "TEXT NOT NULL DEFAULT ''");
        MigrateAddColumnIfNotExists(connection, "ProcessDefinitions", "WarningDaysBeforeDeadline", "INTEGER NOT NULL DEFAULT 0");
        MigrateAddColumnIfNotExists(connection, "ProcessDefinitions", "DepartmentId", "INTEGER NOT NULL DEFAULT 0");

        // LeadTimeMinutesをNULL許容に変更（0=明示的な当日完了、NULL=未設定/フォールバックを区別するため）
        MigrateLeadTimeMinutesNullable(connection);

        // CoolTimeMinutesはMigrateLeadTimeMinutesNullableのテーブル再作成後に追加する（再作成時に列が失われないようにするため）
        MigrateAddColumnIfNotExists(connection, "ProcessDefinitions", "CoolTimeMinutes", "REAL NOT NULL DEFAULT 0");

        // 既存DBのProcessDefinitions.ItemNumberをProductsテーブルに移行
        MigrateAddColumnIfNotExists(connection, "Products", "DisplayName", "TEXT NOT NULL DEFAULT ''");
        MigrateProductsFromProcessDefinitions(connection);
    }

    /// <summary>ProcessDefinitions.ItemNumber が存在する場合、Productsテーブルへ移行する</summary>
    private static void MigrateProductsFromProcessDefinitions(SqliteConnection connection)
    {
        // ItemNumber列が存在しない場合はスキップ（既に移行済み or 新規DB）
        var check = connection.CreateCommand();
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
        var migrate = connection.CreateCommand();
        migrate.CommandText = @"
            INSERT OR IGNORE INTO Products (ProductName, ItemNumber)
            SELECT DISTINCT ItemNumber, COALESCE(ItemNumber, '') FROM ProcessDefinitions
            WHERE ItemNumber != ''";
        migrate.ExecuteNonQuery();
    }

    private static void MigrateRenameColumnIfExists(SqliteConnection connection, string table, string oldColumn, string newColumn)
    {
        var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        using var reader = check.ExecuteReader();
        bool hasOld = false, hasNew = false;
        while (reader.Read())
        {
            var name = reader.GetString(1);
            if (name == oldColumn) hasOld = true;
            if (name == newColumn) hasNew = true;
        }
        if (hasOld && !hasNew)
        {
            var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} RENAME COLUMN {oldColumn} TO {newColumn}";
            alter.ExecuteNonQuery();
        }
    }

    /// <summary>ProcessDefinitions.LeadTimeMinutesがNOT NULLの場合、NULL許容にテーブルを再作成する（既存の0はNULLに変換）</summary>
    private static void MigrateLeadTimeMinutesNullable(SqliteConnection connection)
    {
        var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(ProcessDefinitions)";
        bool isNotNull = false;
        using (var reader = check.ExecuteReader())
        {
            while (reader.Read())
            {
                if (reader.GetString(1) == "LeadTimeMinutes")
                {
                    isNotNull = reader.GetInt32(3) == 1;
                    break;
                }
            }
        }
        if (!isNotNull) return;

        using var transaction = connection.BeginTransaction();

        var createNew = connection.CreateCommand();
        createNew.Transaction = transaction;
        createNew.CommandText = @"
            CREATE TABLE ProcessDefinitions_new (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ItemNumber TEXT NOT NULL,
                ProcessName TEXT NOT NULL,
                LeadTimeMinutes REAL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsVisible INTEGER NOT NULL DEFAULT 1,
                CsvColumnName TEXT NOT NULL DEFAULT '',
                WarningDaysBeforeDeadline INTEGER NOT NULL DEFAULT 0,
                DepartmentId INTEGER NOT NULL DEFAULT 0
            )";
        createNew.ExecuteNonQuery();

        // 既存の0は「未設定（フォールバック対象）」として扱っていたためNULLに変換する
        var copy = connection.CreateCommand();
        copy.Transaction = transaction;
        copy.CommandText = @"
            INSERT INTO ProcessDefinitions_new (Id, ItemNumber, ProcessName, LeadTimeMinutes, SortOrder, IsVisible, CsvColumnName, WarningDaysBeforeDeadline, DepartmentId)
            SELECT Id, ItemNumber, ProcessName, NULLIF(LeadTimeMinutes, 0), SortOrder, IsVisible, CsvColumnName, WarningDaysBeforeDeadline, DepartmentId
            FROM ProcessDefinitions";
        copy.ExecuteNonQuery();

        var dropOld = connection.CreateCommand();
        dropOld.Transaction = transaction;
        dropOld.CommandText = "DROP TABLE ProcessDefinitions";
        dropOld.ExecuteNonQuery();

        var rename = connection.CreateCommand();
        rename.Transaction = transaction;
        rename.CommandText = "ALTER TABLE ProcessDefinitions_new RENAME TO ProcessDefinitions";
        rename.ExecuteNonQuery();

        transaction.Commit();
    }

    private static void MigrateAddColumnIfNotExists(SqliteConnection connection, string table, string column, string definition)
    {
        var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == column) return;
        }
        var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }
}
