namespace ShipmentCalendar.Models;

/// <summary>アプリ設定（JSONファイルで永続化）</summary>
public class AppSettings
{
    /// <summary>ODBC DSN名（例: DrSum_WORKDB_YD）</summary>
    public string OdbcDsn { get; set; } = string.Empty;
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
    private int _dayMinutes = 420;
    /// <summary>1営業日あたりの稼働時間（分）。工程期限日・工程バーの日割り計算に使う。
    /// 0以下・1440(24時間)超は後続のゼロ除算や無意味な設定値を防ぐため既定値(420)に補正する
    /// （設定ファイルの直接編集等を想定）</summary>
    public int DayMinutes {
        get => _dayMinutes;
        set => _dayMinutes = value > 0 && value <= 1440 ? value : 420;
    }
    /// <summary>未完了工程の表示日付を完了必須日にするか（false=着手必須日を表示）</summary>
    public bool ShowDueDateForNotStarted { get; set; } = false;
    /// <summary>注文一覧の並び順</summary>
    public SortMode SortMode { get; set; } = SortMode.DeliveryDate;

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

    /// <summary>メイン画面の固定列（出荷日〜計画数）のフォントサイズ</summary>
    public double FixedColumnFontSize { get; set; } = 12;
    /// <summary>メイン画面の工程列（工程名・期限日・標準時間）のフォントサイズ</summary>
    public double ProcessColumnFontSize { get; set; } = 11;
    /// <summary>工程バーのテキストフォントサイズ</summary>
    public double ProcessBarFontSize { get; set; } = 10;
    /// <summary>工程列に「期限日」行を表示するか</summary>
    public bool ShowProcessDate { get; set; } = true;
    /// <summary>工程列に「標準時間（必要時間）」行を表示するか</summary>
    public bool ShowProcessRequiredHours { get; set; } = true;
    /// <summary>必要時間の表示単位。true=分表記、false=時間表記</summary>
    public bool ShowRequiredTimeInMinutes { get; set; } = false;
    /// <summary>メイン画面に「工程バー」列を表示するか</summary>
    public bool ShowProcessBar { get; set; } = true;
    /// <summary>メイン画面に「工程列（1工程1列）」を表示するか</summary>
    public bool ShowProcessColumns { get; set; } = false;
    /// <summary>メイン画面の行の高さ（px）。0=自動計算</summary>
    public double ManualRowHeight { get; set; } = 0;

    /// <summary>ODBC接続設定が入力済みか</summary>
    public bool IsOdbcConfigured => !string.IsNullOrEmpty(OdbcDsn);
}

/// <summary>注文一覧の並び順</summary>
public enum SortMode
{
    DeliveryDate,     // 出荷日順
    CompletionDate,   // 完了日順
    ProcessDeadline,  // 工程期限順（次の未完了工程の期限日）
}
