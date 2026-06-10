namespace ShipmentCalendar.Models;

/// <summary>アプリ設定（JSONファイルで永続化）</summary>
public class AppSettings
{
    /// <summary>ODBC接続方式（"Dsn"=DSN名を使用、"Direct"=DSNレス接続）</summary>
    public string OdbcConnectionMode { get; set; } = "Dsn";
    /// <summary>ODBC DSN名（例: DrSum_WORKDB_YD）</summary>
    public string OdbcDsn { get; set; } = string.Empty;
    /// <summary>DSNレス接続時のサーバー名/IPアドレス</summary>
    public string OdbcServer { get; set; } = string.Empty;
    /// <summary>DSNレス接続時のポート番号</summary>
    public string OdbcPort { get; set; } = string.Empty;
    /// <summary>DSNレス接続時のデータベース名</summary>
    public string OdbcDatabase { get; set; } = string.Empty;
    /// <summary>ODBCユーザーID</summary>
    public string OdbcUserId { get; set; } = string.Empty;
    /// <summary>ODBCパスワード（社内利用前提で平文保存）</summary>
    public string OdbcPassword { get; set; } = string.Empty;
    /// <summary>自動更新間隔（分）。0=自動更新なし</summary>
    public int AutoRefreshMinutes { get; set; } = 5;
    /// <summary>表示する納期の範囲（今日から何日先まで）</summary>
    public int DeliveryDateRangeDays { get; set; } = 90;
    /// <summary>表示する納期の範囲（今日から何日前まで）</summary>
    public int DeliveryDatePastDays { get; set; } = 0;

    /// <summary>ODBC接続設定が入力済みか（接続方式に応じて必須項目を判定）</summary>
    public bool IsOdbcConfigured =>
        OdbcConnectionMode == "Direct"
            ? !string.IsNullOrEmpty(OdbcServer) && !string.IsNullOrEmpty(OdbcDatabase)
            : !string.IsNullOrEmpty(OdbcDsn);
}
