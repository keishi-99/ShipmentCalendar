using Microsoft.Data.Sqlite;
using ShipmentCalendar.Services;
using System.IO;

namespace ShipmentCalendar.Data;

/// <summary>共有データフォルダ内の複数DBファイル（process.db/department.db/holiday.db）に共通する
/// パス解決・接続文字列生成をまとめる。障害発生時の被害範囲を関連テーブルの単位に閉じ込めるため、
/// 用途ごとにDBファイルを分けている（1ファイルに全テーブルを同居させない）</summary>
public static class SharedDatabase {
    private static readonly string? _dataDir = AppSettingsService.GetSharedDataDir();

    /// <summary>共有データフォルダが設定されているか。falseの場合、マスタDBを扱う機能は使用できない</summary>
    public static bool IsAvailable => _dataDir != null;

    public static string ConnectionStringFor(string fileName) => _dataDir != null
        ? new SqliteConnectionStringBuilder { DataSource = Path.Combine(_dataDir, fileName), DefaultTimeout = 5 }.ToString()
        : throw new InvalidOperationException("共有データフォルダが設定されていません。設定 > 基本設定 から設定してください。");

    public static void EnsureDataDirExists() {
        if (_dataDir != null) Directory.CreateDirectory(_dataDir);
    }
}
