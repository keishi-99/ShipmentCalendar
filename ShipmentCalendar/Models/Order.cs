namespace ShipmentCalendar.Models;

/// <summary>注文（出荷予定）エンティティ</summary>
public class Order
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    /// <summary>機種コード（製品/半製品の区分判定に使用）</summary>
    public string ModelCode { get; set; } = string.Empty;
    public string ManufactureNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public DateOnly DeliveryDate { get; set; }
    /// <summary>完了日（出荷日から設定の営業日数だけ前の日）</summary>
    public DateOnly CompletionDate { get; set; }
    public int PlannedQuantity { get; set; }
    public List<OrderProcess> Processes { get; set; } = [];

    public bool IsAllCompleted =>
        Processes.Count > 0 && Processes.All(p => p.Status == ProcessStatus.Completed);

    public bool HasOverdue =>
        Processes.Any(p => p.Status == ProcessStatus.Overdue);
}
