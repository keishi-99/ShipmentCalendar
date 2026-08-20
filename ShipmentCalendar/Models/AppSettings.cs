namespace ShipmentCalendar.Models;

/// <summary>アプリ設定（JSONファイルで永続化）</summary>
public class AppSettings
{
    /// <summary>ODBC DSN名（例: DrSum_WORKDB_YD）</summary>
    public string OdbcDsn { get; set; } = string.Empty;
    /// <summary>ODBCから受注データ・休日データを取得する際の絞り込みに使う工場番号</summary>
    public string OdbcFactoryNumber { get; set; } = string.Empty;
    /// <summary>マスタDB・編集ロックファイルを配置する共有フォルダのパス（例: \\server\share\ShipmentCalendarData）。
    /// 未設定（空欄）の場合、マスタDBを扱う機能は使用できない（ローカルフォルダへのフォールバックは行わない）。
    /// 変更はアプリ再起動後に反映される</summary>
    public string SharedDataFolderPath { get; set; } = string.Empty;
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
    /// <summary>製品/半製品区分フィルター: "全て" / "製品" / "半製品" / "半製品（工程未登録）" / "どちらでもない"</summary>
    public string FilterProductCategory { get; set; } = "全て";

    /// <summary>メイン画面の「出荷日」列を表示するか</summary>
    public bool ShowColumnDeliveryDate { get; set; } = true;
    /// <summary>メイン画面の「完了期限日」列を表示するか</summary>
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

    /// <summary>部署別締切集中度カレンダーで「やや集中」と判定する充足率（%）＝合計必要時間÷(基本人数×1日の稼働時間)のしきい値</summary>
    public double CongestionCautionPercent { get; set; } = 80;
    /// <summary>部署別締切集中度カレンダーで「集中」と判定する充足率（%）のしきい値</summary>
    public double CongestionConcentratedPercent { get; set; } = 100;

    /// <summary>工程別ボトルネック分析：標準超過と判定するしきい値のモード（割合／固定分）</summary>
    public BottleneckThresholdMode BottleneckOverThresholdMode { get; set; } = BottleneckThresholdMode.Percent;
    /// <summary>工程別ボトルネック分析：標準超過と判定するしきい値（モードがPercentなら標準時間に対する%、FixedMinutesなら固定分）</summary>
    public double BottleneckOverThresholdValue { get; set; } = 110;
    /// <summary>工程別ボトルネック分析：標準未達と判定するしきい値のモード（割合／固定分）</summary>
    public BottleneckThresholdMode BottleneckUnderThresholdMode { get; set; } = BottleneckThresholdMode.Percent;
    /// <summary>工程別ボトルネック分析：標準未達と判定するしきい値（モードがPercentなら標準時間に対する%、FixedMinutesなら固定分）</summary>
    public double BottleneckUnderThresholdValue { get; set; } = 80;
}

/// <summary>注文一覧の並び順</summary>
public enum SortMode
{
    DeliveryDate,     // 出荷日順
    CompletionDate,   // 完了日順
    ProcessDeadline,  // 工程期限順（次の未完了工程の期限日）
}

/// <summary>工程の表示モード</summary>
public enum ProcessMode
{
    Bar,  // 工程バー列で表示
    List, // 工程1つ1列で表示
}

/// <summary>工程別ボトルネック分析の超過・未達判定しきい値のモード</summary>
public enum BottleneckThresholdMode
{
    Percent,      // 標準時間に対する割合(%)
    FixedMinutes, // 標準時間からの固定分数
}
