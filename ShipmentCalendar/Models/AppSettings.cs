namespace ShipmentCalendar.Models;

/// <summary>アプリ設定（JSONファイルで永続化）</summary>
public class AppSettings
{
    /// <summary>ODBC DSN名（例: DrSum_WORKDB_YD）</summary>
    public string OdbcDsn { get; set; } = string.Empty;
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
}
