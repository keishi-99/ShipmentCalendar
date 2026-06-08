using ShipmentCalendar.Models;

namespace ShipmentCalendar.Services;

/// <summary>営業日計算サービス（休日・土日を除く）</summary>
public class BusinessDayCalculator {
    private readonly HashSet<DateOnly> _holidays;

    public BusinessDayCalculator(IEnumerable<Holiday> holidays) {
        _holidays = holidays.Select(h => h.Date).ToHashSet();
    }

    /// <summary>基準日からN営業日前の日付を返す</summary>
    public DateOnly SubtractBusinessDays(DateOnly baseDate, int businessDays) {
        var current = baseDate;
        var remaining = businessDays;

        while (remaining > 0) {
            current = current.AddDays(-1);
            if (IsBusinessDay(current))
                remaining--;
        }
        return current;
    }

    /// <summary>指定日が営業日かどうかを判定</summary>
    public bool IsBusinessDay(DateOnly date) {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;
        return !_holidays.Contains(date);
    }

    /// <summary>
    /// 工程定義から期限日を直列逆算で計算する。
    /// SortOrderの大きい工程（納期に近い）から順に逆算し、基準日を更新していく。
    /// LeadTimeDays=0の工程は前の工程と同じ期限日になる。
    /// </summary>
    public List<OrderProcess> BuildProcesses(Order order, IEnumerable<ProcessDefinition> definitions) {
        // 同一 ProcessName が複数ある場合（名称マスタ重複等）は先着優先で1件に集約
        var importedStatuses = order.Processes
            .GroupBy(p => p.ProcessName)
            .ToDictionary(g => g.Key, g => g.First().Status);

        var sorted = definitions.OrderBy(d => d.SortOrder).ToList();
        var results = new List<OrderProcess>(sorted.Count);

        // 納期を起点に直列逆算（SortOrder降順＝納期に近い順に処理）
        var baseDate = order.DeliveryDate;
        for (int i = sorted.Count - 1; i >= 0; i--) {
            var def = sorted[i];
            var dueDate = SubtractBusinessDays(baseDate, def.LeadTimeDays);
            results.Insert(0, new OrderProcess {
                ProcessName = def.ProcessName,
                DueDate = dueDate,
                Status = importedStatuses.TryGetValue(def.ProcessName, out var s) && s == ProcessStatus.Completed
                    ? ProcessStatus.Completed
                    : ProcessStatus.NotStarted,
                SortOrder = i,
                DepartmentId = def.DepartmentId
            });
            // LeadTimeDays=0のときbaseDateは変えない（同日に複数工程）
            if (def.LeadTimeDays > 0)
                baseDate = dueDate;
        }

        return results;
    }

    /// <summary>今日の日付を基準に工程ステータスを自動判定する</summary>
    public ProcessStatus DetermineStatus(OrderProcess process, DateOnly today, int warningDays = 0) {
        if (process.Status == ProcessStatus.Completed)
            return ProcessStatus.Completed;

        if (today > process.DueDate)
            return ProcessStatus.Overdue;

        if (warningDays > 0 && (process.DueDate.DayNumber - today.DayNumber) <= warningDays)
            return ProcessStatus.Warning;

        if (today == process.DueDate)
            return ProcessStatus.InProgress;

        return ProcessStatus.NotStarted;
    }
}
