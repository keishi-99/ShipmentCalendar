namespace ShipmentCalendar.Models;

/// <summary>工程マスタ定義（製品ごとに設定）</summary>
public class ProcessDefinition
{
    public int Id { get; set; }
    public string ItemNumber { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    /// <summary>この工程の所要時間（分単位・小数あり）。1日480分換算で営業日を算出。0=当日中に完了。null=未設定（CSV/ODBC側の標準値にフォールバック）</summary>
    public double? LeadTimeMinutes { get; set; }
    public int SortOrder { get; set; }
    /// <summary>一覧に表示するか</summary>
    public bool IsVisible { get; set; } = true;
    /// <summary>指示先番号（取引先コード）。工程ごとに一意な識別子</summary>
    public string DestinationCode { get; set; } = string.Empty;
    /// <summary>期限日まで何日以内で警告するか（0=警告なし）</summary>
    public int WarningDaysBeforeDeadline { get; set; } = 0;
    /// <summary>担当部署ID（0=未設定）</summary>
    public int DepartmentId { get; set; } = 0;
    /// <summary>この工程の後に発生する固定の待機時間（分・数量に依存しない）。0=なし</summary>
    public double CoolTimeMinutes { get; set; } = 0;
    /// <summary>この工程の後の外注待ち等で発生する営業日数。0=なし</summary>
    public int OutsourceLeadDays { get; set; } = 0;
}
