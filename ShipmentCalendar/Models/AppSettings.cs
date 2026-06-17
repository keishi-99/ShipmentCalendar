namespace ShipmentCalendar.Models;

/// <summary>アプリ設定（JSONファイルで永続化）</summary>
public class AppSettings
{
    /// <summary>ODBC DSN名（例: DrSum_WORKDB_YD）</summary>
    public string OdbcDsn { get; set; } = string.Empty;
    /// <summary>ODBCユーザーID</summary>
    public string OdbcUserId { get; set; } = string.Empty;
    /// <summary>ODBCパスワード（保存時はDPAPIで暗号化される）</summary>
    public string OdbcPassword { get; set; } = string.Empty;
    /// <summary>休日取得（VP_カレンダ情報_YD）の絞り込みに使う工場番号</summary>
    public string OdbcFactoryNumber { get; set; } = string.Empty;
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
    /// <summary>注文一覧の並び順を「次の未完了工程の期限日」にするか（false=出荷日順）</summary>
    public bool SortByProcessDeadline { get; set; } = false;

    /// <summary>メイン画面の「出荷日」列を表示するか</summary>
    public bool ShowColumnDeliveryDate { get; set; } = true;
    /// <summary>メイン画面の「完了日」列を表示するか</summary>
    public bool ShowColumnCompletionDate { get; set; } = true;
    /// <summary>メイン画面の「品目番号」列を表示するか</summary>
    public bool ShowColumnItemNumber { get; set; } = true;
    /// <summary>メイン画面の「機種コード」列を表示するか</summary>
    public bool ShowColumnModelCode { get; set; } = true;
    /// <summary>メイン画面の「品目名」列を表示するか</summary>
    public bool ShowColumnProductName { get; set; } = true;
    /// <summary>メイン画面の「製番」列を表示するか</summary>
    public bool ShowColumnManufactureNumber { get; set; } = true;
    /// <summary>メイン画面の「計画数」列を表示するか</summary>
    public bool ShowColumnPlannedQuantity { get; set; } = true;

    /// <summary>メイン画面のフォントファミリー</summary>
    public string FontFamily { get; set; } = "Yu Gothic UI";
    /// <summary>メイン画面の固定列（出荷日〜計画数）のフォントサイズ</summary>
    public double FixedColumnFontSize { get; set; } = 12;
    /// <summary>メイン画面の工程列（工程名・期限日・標準時間）のフォントサイズ</summary>
    public double ProcessColumnFontSize { get; set; } = 11;
    /// <summary>工程列に「期限日」行を表示するか</summary>
    public bool ShowProcessDate { get; set; } = true;
    /// <summary>工程列に「標準時間（必要時間）」行を表示するか</summary>
    public bool ShowProcessRequiredHours { get; set; } = true;
    /// <summary>メイン画面に「工程バー」列を表示するか</summary>
    public bool ShowProcessBar { get; set; } = true;
    /// <summary>メイン画面に「工程列（1工程1列）」を表示するか</summary>
    public bool ShowProcessColumns { get; set; } = false;
    /// <summary>メイン画面の行の高さ（px）。0=自動計算</summary>
    public double ManualRowHeight { get; set; } = 0;

    /// <summary>ODBC接続設定が入力済みか</summary>
    public bool IsOdbcConfigured => !string.IsNullOrEmpty(OdbcDsn);
}
