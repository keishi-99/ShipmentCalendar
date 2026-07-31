namespace ShipmentCalendar.Models;

/// <summary>担当部署マスタ</summary>
public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    /// <summary>基本人数。0=未設定（締切集中度の充足率判定はNormal固定になる）</summary>
    public int Headcount { get; set; }
}
