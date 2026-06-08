namespace ShipmentCalendar.Models;

/// <summary>製品マスタ（製品名と品目番号の対応）</summary>
public class Product
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    /// <summary>CSVの品目番号との部分一致キー</summary>
    public string ItemNumber { get; set; } = string.Empty;
}
