namespace ShipmentCalendar.Models;

/// <summary>工程マスタ定義（製品ごとに設定）</summary>
public class ProcessDefinition
{
    public int Id { get; set; }
    public string ItemNumber { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    /// <summary>この工程の所要営業日数（0=当日中に完了）</summary>
    public int LeadTimeDays { get; set; }
    public int SortOrder { get; set; }
    /// <summary>一覧に表示するか</summary>
    public bool IsVisible { get; set; } = true;
    /// <summary>CSV上の完了日列名（空の場合はCSV連携なし）</summary>
    public string CsvColumnName { get; set; } = string.Empty;
    /// <summary>期限日まで何日以内で警告するか（0=警告なし）</summary>
    public int WarningDaysBeforeDeadline { get; set; } = 0;
}
