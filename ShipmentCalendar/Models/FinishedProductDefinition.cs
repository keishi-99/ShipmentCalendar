namespace ShipmentCalendar.Models;

/// <summary>製品マスタ（品目番号の先頭一致パターンで「製品」を判定するための登録）</summary>
public class FinishedProductDefinition
{
    public int Id { get; set; }
    /// <summary>表示名</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>品目番号の先頭一致パターン</summary>
    public string ItemNumberPrefix { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
