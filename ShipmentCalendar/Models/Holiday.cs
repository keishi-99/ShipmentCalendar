namespace ShipmentCalendar.Models;

/// <summary>会社休日マスタ</summary>
public class Holiday
{
    public DateOnly Date { get; set; }
    public string Description { get; set; } = string.Empty;
}
