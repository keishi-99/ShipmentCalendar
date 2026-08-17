namespace ShipmentCalendar.Models;

/// <summary>進捗ダッシュボードの集計結果</summary>
public class DashboardSummary
{
    public int TotalCount { get; init; }
    public int CompletedCount { get; init; }
    public int OverdueCount { get; init; }
    public int WarningCount { get; init; }
    public List<DashboardDepartmentRow> DepartmentRows { get; init; } = [];
    public List<DashboardOrderRow> OrderRows { get; init; } = [];

    public double CompletedRate => TotalCount > 0 ? (double)CompletedCount / TotalCount * 100 : 0;
    public string CompletedRateText => TotalCount > 0 ? $"{CompletedRate:F0}%" : "-";
}

/// <summary>進捗ダッシュボードの部署別行</summary>
public class DashboardDepartmentRow
{
    public int DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    /// <summary>この部署が担当する工程の総数</summary>
    public int TotalProcessCount { get; init; }
    /// <summary>完了済みの工程数</summary>
    public int CompletedProcessCount { get; init; }
    /// <summary>この部署が担当する工程のうち、未完了の件数</summary>
    public int RemainingProcessCount { get; init; }
    /// <summary>締切（DueDate）を過ぎても未完了の工程の件数</summary>
    public int OverdueProcessCount { get; init; }

    /// <summary>この部署が担当する全工程一覧（ドリルダウン表示用）</summary>
    public List<DashboardProcessItem> AllItems { get; init; } = [];
    /// <summary>完了済み工程一覧（ドリルダウン表示用）。直近に完了したものほど先頭</summary>
    public List<DashboardProcessItem> CompletedItems { get; init; } = [];
    /// <summary>未完了工程一覧（ドリルダウン表示用）。超過中のものを先頭にし、次に締切が古い順</summary>
    public List<DashboardProcessItem> RemainingItems { get; init; } = [];
    /// <summary>締切を過ぎても未完了の工程一覧（ドリルダウン表示用）。締切が古い順</summary>
    public List<DashboardProcessItem> OverdueItems { get; init; } = [];
}

/// <summary>進捗ダッシュボードの注文別行</summary>
public class DashboardOrderRow
{
    public required Order Order { get; init; }

    public string ManufactureNumber => Order.ManufactureNumber;
    public string ProductName => Order.ProductName;
    public DateOnly DeliveryDate => Order.DeliveryDate;
    /// <summary>この注文の工程の総数</summary>
    public int TotalProcessCount { get; init; }
    /// <summary>完了済みの工程数</summary>
    public int CompletedProcessCount { get; init; }
    /// <summary>未完了の工程数</summary>
    public int RemainingProcessCount { get; init; }
    /// <summary>締切（DueDate）を過ぎても未完了の工程の件数</summary>
    public int OverdueProcessCount { get; init; }

    /// <summary>この注文の全工程一覧（ドリルダウン表示用）</summary>
    public List<DashboardProcessItem> AllItems { get; init; } = [];
    /// <summary>完了済み工程一覧（ドリルダウン表示用）。直近に完了したものほど先頭</summary>
    public List<DashboardProcessItem> CompletedItems { get; init; } = [];
    /// <summary>未完了工程一覧（ドリルダウン表示用）。超過中のものを先頭にし、次に締切が古い順</summary>
    public List<DashboardProcessItem> RemainingItems { get; init; } = [];
    /// <summary>締切を過ぎても未完了の工程一覧（ドリルダウン表示用）。締切が古い順</summary>
    public List<DashboardProcessItem> OverdueItems { get; init; } = [];
}

/// <summary>進捗ダッシュボードの部署別明細1行（工程1件分）</summary>
public class DashboardProcessItem
{
    public required Order Order { get; init; }
    public required OrderProcess Process { get; init; }

    public string ManufactureNumber => Order.ManufactureNumber;
    public string ProductName => Order.ProductName;
    public string ProcessName => Process.ProcessName;
    public string StatusText => Process.Status switch {
        ProcessStatus.Overdue => "超過",
        ProcessStatus.Warning => "警告",
        ProcessStatus.Completed => "完了",
        _ => string.Empty
    };
    /// <summary>StatusTextの表示色（16進カラーコード）。ダッシュボード上部の集計カードと同系色に揃えている</summary>
    public string StatusColor => Process.Status switch {
        ProcessStatus.Overdue => "#B71C1C",
        ProcessStatus.Warning => "#8D6E00",
        ProcessStatus.Completed => "#2E7D32",
        _ => "#666666"
    };
    /// <summary>完了済み工程は実際の完了日、未完了工程は完了期限日を表示する</summary>
    public string DateText => Process.Status == ProcessStatus.Completed && Process.ActualDate is { } actual
        ? $"完了 {actual:M/d}"
        : $"期限 {Process.DueDate:M/d}";
}
