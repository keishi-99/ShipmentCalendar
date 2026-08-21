using ShipmentCalendar.Models;

namespace ShipmentCalendar.Services;

/// <summary>注文一覧のフィルター条件（MainViewModelのFilter系プロパティをまとめたもの）</summary>
public record OrderFilterCriteria {
    public string ItemNumber { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string ManufactureNumber { get; init; } = string.Empty;
    public DateTime? DeliveryFrom { get; init; }
    public DateTime? DeliveryTo { get; init; }
    public bool HideCompleted { get; init; }
    public bool OverdueOnly { get; init; }
    public bool WarningOnly { get; init; }
    public bool TodayTaskOnly { get; init; }
    public bool CompletedOnly { get; init; }
    public bool NotStartedOnly { get; init; }
    public string ProductCategory { get; init; } = "全て";
    public int DepartmentId { get; init; }
}

/// <summary>注文一覧のフィルター・並び替えロジック（MainViewModel.ApplyFilterから抽出）</summary>
public static class OrderFilterService {
    public static List<Order> Apply(
        IEnumerable<Order> allOrders,
        OrderFilterCriteria criteria,
        ProductCategoryClassifier classifier,
        SortMode sortMode,
        bool showDueDateForNotStarted) {
        var result = allOrders.AsEnumerable();

        if (!string.IsNullOrEmpty(criteria.ItemNumber))
            result = result.Where(o => o.ItemNumber.Contains(criteria.ItemNumber, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(criteria.ProductName))
            result = result.Where(o => o.ProductName.Contains(criteria.ProductName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(criteria.ManufactureNumber))
            result = result.Where(o => o.ManufactureNumber.Contains(criteria.ManufactureNumber, StringComparison.OrdinalIgnoreCase));

        if (criteria.DeliveryFrom.HasValue)
            result = result.Where(o => o.DeliveryDate >= DateOnly.FromDateTime(criteria.DeliveryFrom.Value));

        if (criteria.DeliveryTo.HasValue)
            result = result.Where(o => o.DeliveryDate <= DateOnly.FromDateTime(criteria.DeliveryTo.Value));

        if (criteria.HideCompleted)
            result = result.Where(o => o.Processes.Count == 0 || o.Processes.Any(p => p.Status != ProcessStatus.Completed));

        if (criteria.OverdueOnly || criteria.WarningOnly || criteria.TodayTaskOnly || criteria.CompletedOnly || criteria.NotStartedOnly) {
            var today = DateOnly.FromDateTime(DateTime.Today);
            result = result.Where(o => {
                var isOverdue = criteria.OverdueOnly && o.HasOverdue;
                var isWarning = criteria.WarningOnly && o.Processes.Any(p => p.Status == ProcessStatus.Warning);
                var next = GetNextIncompleteProcess(o);
                var isToday = criteria.TodayTaskOnly && next != null && today >= next.StartDate && today <= next.DueDate;
                // 工程が1件も登録されていない注文をnext==nullだけで「完了」と誤判定しないようCountもチェック
                var isCompleted = criteria.CompletedOnly && o.Processes.Count > 0 && next == null;
                var isNotStarted = criteria.NotStartedOnly && next?.Status == ProcessStatus.NotStarted;
                return isOverdue || isWarning || isToday || isCompleted || isNotStarted;
            });
        }

        // 製品/半製品/どちらでもないフィルター（機種コード登録マスタの区分で判定）
        if (criteria.ProductCategory == "製品")
            result = result.Where(o => classifier.Classify(o) == ProductCategoryClassifier.Product);
        else if (criteria.ProductCategory == "半製品")
            result = result.Where(o => classifier.Classify(o) == ProductCategoryClassifier.SemiProduct);
        else if (criteria.ProductCategory == "半製品（工程未登録）")
            result = result.Where(o => classifier.IsUnregisteredSemiProduct(o));
        else if (criteria.ProductCategory == "どちらでもない")
            result = result.Where(o => classifier.Classify(o) == ProductCategoryClassifier.Other);

        // 担当部署フィルター：未完了工程のうち SortOrder 最小のものが選択部署の行のみ表示
        if (criteria.DepartmentId > 0) {
            result = result.Where(o => GetNextIncompleteProcess(o)?.DepartmentId == criteria.DepartmentId);
        }

        return sortMode switch {
            SortMode.CompletionDate  => result.OrderBy(o => o.CompletionDate).ToList(),
            SortMode.ProcessDeadline => result.OrderBy(o => GetNextProcessSortDate(o, showDueDateForNotStarted)).ToList(),
            _                        => result.OrderBy(o => o.DeliveryDate).ToList(),
        };
    }

    /// <summary>注文の「次の未完了工程」の必須日（表示設定に応じてDueDate/StartDate）を返す。
    /// 全工程完了済みならDateOnly.MaxValue</summary>
    public static DateOnly GetNextProcessSortDate(Order o, bool showDueDateForNotStarted) {
        var next = GetNextIncompleteProcess(o);
        if (next == null) return DateOnly.MaxValue;
        return showDueDateForNotStarted ? next.DueDate : next.StartDate;
    }

    /// <summary>SortOrderが最小の未完了工程を返す。全工程完了済みならnull</summary>
    private static OrderProcess? GetNextIncompleteProcess(Order o) =>
        o.Processes
            .Where(p => p.Status != ProcessStatus.Completed)
            .OrderBy(p => p.SortOrder)
            .FirstOrDefault();
}
