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
    /// 工程定義から期限日を計算する。
    /// 全工程の累積必要時間を前向きに集計し、480分（8時間）単位の日チャンクに割り当て、
    /// 納期から逆算して各工程の予定日を決定する。
    /// 例: 作業A=120分, B=100分, C=300分 → A,B は同日、C は翌日（=納期当日）
    /// </summary>
    public List<OrderProcess> BuildProcesses(Order order, IEnumerable<ProcessDefinition> definitions)
    {
        // 同一 ProcessName が複数ある場合は先着優先で1件に集約
        var importedStatuses = order.Processes
            .GroupBy(p => p.ProcessName)
            .ToDictionary(g => g.Key, g => g.First().Status);

        var sorted = definitions.OrderBy(d => d.SortOrder).ToList();
        if (!sorted.Any()) return new List<OrderProcess>();

        // 前向きに累積分数を計算し、480分ごとの日チャンク番号を割り当て
        double running = 0;
        var chunks = new int[sorted.Count];
        for (int i = 0; i < sorted.Count; i++)
        {
            double minutes = sorted[i].LeadTimeMinutes * order.PlannedQuantity;
            running += minutes;
            if (minutes <= 0)
            {
                // 0分の工程は前の工程と同じチャンク（または1）
                chunks[i] = i > 0 ? chunks[i - 1] : 1;
            }
            else
            {
                chunks[i] = (int)Math.Ceiling(running / 480.0);
                // 1日以上かかる工程はチャンク末尾にrunningを揃え、後続工程を次の日へ押し出す
                if (minutes >= 480)
                    running = chunks[i] * 480.0;
            }
        }
        int totalChunks = Math.Max(1, chunks.Max());

        // 各工程の予定日 = 納期 - (totalChunks - chunk) 営業日
        var results = new List<OrderProcess>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            var def = sorted[i];
            double requiredMinutes = def.LeadTimeMinutes * order.PlannedQuantity;
            var dueDate = SubtractBusinessDays(order.DeliveryDate, totalChunks - chunks[i]);
            results.Add(new OrderProcess {
                ProcessName = def.ProcessName,
                DueDate = dueDate,
                Status = importedStatuses.TryGetValue(def.ProcessName, out var s) && s == ProcessStatus.Completed
                    ? ProcessStatus.Completed
                    : ProcessStatus.NotStarted,
                SortOrder = def.SortOrder,
                DepartmentId = def.DepartmentId,
                RequiredMinutes = requiredMinutes
            });
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
