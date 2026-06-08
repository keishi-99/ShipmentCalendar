namespace ShipmentCalendar.Models;

/// <summary>注文に紐づく工程インスタンス</summary>
public class OrderProcess
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public ProcessStatus Status { get; set; } = ProcessStatus.NotStarted;
    public int SortOrder { get; set; }
    /// <summary>担当部署ID（ProcessDefinitionから引き継ぐ）</summary>
    public int DepartmentId { get; set; } = 0;
}

public enum ProcessStatus
{
    NotStarted,  // 未着手
    InProgress,  // 進行中
    Warning,     // 期限間近
    Completed,   // 完了
    Overdue      // 超過
}
