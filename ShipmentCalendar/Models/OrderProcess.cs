namespace ShipmentCalendar.Models;

/// <summary>注文に紐づく工程インスタンス</summary>
public class OrderProcess
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    /// <summary>着手予定日（開始日）。DueDateより前になる場合がある（480分超えの工程）</summary>
    public DateOnly StartDate { get; set; }
    public DateOnly DueDate { get; set; }
    /// <summary>実際の完了日（受入実績の受入日）。完了工程のみセット</summary>
    public DateOnly? ActualDate { get; set; }
    public ProcessStatus Status { get; set; } = ProcessStatus.NotStarted;
    public int SortOrder { get; set; }
    /// <summary>担当部署ID（ProcessDefinitionから引き継ぐ）</summary>
    public int DepartmentId { get; set; } = 0;
    /// <summary>必要時間（分）= (段取時間 + 作業時間) × 計画数。0=未設定</summary>
    public double RequiredMinutes { get; set; } = 0;
}

public enum ProcessStatus
{
    NotStarted,  // 未着手
    InProgress,  // 進行中
    Warning,     // 期限間近
    Completed,   // 完了
    Overdue      // 超過
}
