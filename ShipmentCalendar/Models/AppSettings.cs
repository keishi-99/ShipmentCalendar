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
    /// <summary>ODBCパスワード（保存時はDPAPIで暗号化される）</summary>
    public string OdbcPassword { get; set; } = string.Empty;
    /// <summary>自動更新間隔（分）。0=自動更新なし</summary>
    public int AutoRefreshMinutes { get; set; } = 5;
    /// <summary>表示する納期の範囲（今日から何日先まで）</summary>
    public int DeliveryDateRangeDays { get; set; } = 90;
    /// <summary>表示する納期の範囲（今日から何日前まで）</summary>
    public int DeliveryDatePastDays { get; set; } = 0;
    /// <summary>完了日の算出に使う、出荷日からの営業日数（出荷日からこの日数だけ前の営業日を完了日とする）</summary>
    public int CompletionDateLeadDays { get; set; } = 1;
    /// <summary>未完了工程の表示日付を完了必須日にするか（false=着手必須日を表示）</summary>
    public bool ShowDueDateForNotStarted { get; set; } = false;

    /// <summary>ODBC接続設定が入力済みか（接続方式に応じて必須項目を判定）</summary>
    public bool IsOdbcConfigured =>
        OdbcConnectionMode == "Direct"
            ? !string.IsNullOrEmpty(OdbcServer) && !string.IsNullOrEmpty(OdbcDatabase)
            : !string.IsNullOrEmpty(OdbcDsn);
}
