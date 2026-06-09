namespace ShipmentCalendar.Models;

/// <summary>工程マスタ定義（製品ごとに設定）</summary>
public class ProcessDefinition
{
    public int Id { get; set; }
    public string ItemNumber { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    /// <summary>この工程の所要時間（分単位・小数あり）。1日480分換算で営業日を算出。0=当日中に完了</summary>
    public double LeadTimeMinutes { get; set; }
    public int SortOrder { get; set; }
    /// <summary>一覧に表示するか</summary>
    public bool IsVisible { get; set; } = true;
    /// <summary>CSV上の完了日列名（空の場合はCSV連携なし）</summary>
    public string CsvColumnName { get; set; } = string.Empty;
    /// <summary>期限日まで何日以内で警告するか（0=警告なし）</summary>
    public int WarningDaysBeforeDeadline { get; set; } = 0;
    /// <summary>担当部署ID（0=未設定）</summary>
    public int DepartmentId { get; set; } = 0;
}
