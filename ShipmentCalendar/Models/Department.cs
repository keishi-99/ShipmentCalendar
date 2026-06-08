namespace ShipmentCalendar.Models;

/// <summary>担当部署マスタ</summary>
public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
