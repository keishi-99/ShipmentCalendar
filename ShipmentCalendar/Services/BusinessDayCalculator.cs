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
    /// <summary>
    /// completedByDestNumber: 完了済み指示先番号→受入日 のマッピング（指示先番号は工程ごとに一意）
    /// </summary>
    public List<OrderProcess> BuildProcesses(Order order, IEnumerable<ProcessDefinition> definitions, Dictionary<string, DateOnly?> completedByDestNumber) {
        var sorted = definitions.OrderBy(d => d.SortOrder).ToList();
        if (!sorted.Any()) return new List<OrderProcess>();

        // 前向きに累積分数を計算し、480分ごとの日チャンク番号を割り当て
        double running = 0;
        var chunks = new int[sorted.Count];
        for (int i = 0; i < sorted.Count; i++) {
            double minutes = (sorted[i].LeadTimeMinutes ?? 0) * order.PlannedQuantity;
            running += minutes;
            if (minutes <= 0) {
                // 0分の工程は前の工程と同じチャンク（または1）
                chunks[i] = i > 0 ? chunks[i - 1] : 1;
            }
            else {
                chunks[i] = (int)Math.Ceiling(running / 480.0);
                // 1日以上かかる工程はチャンク末尾にrunningを揃え、後続工程を次の日へ押し出す
                if (minutes >= 480)
                    running = chunks[i] * 480.0;
            }

            // クールタイム（数量に依存しない固定の待機時間）を加算。
            // 単独では翌日に繰り越さず、その日のチャンク上限で切り詰める
            if (sorted[i].CoolTimeMinutes > 0) {
                running += sorted[i].CoolTimeMinutes;
                var dayLimit = chunks[i] * 480.0;
                if (running > dayLimit)
                    running = dayLimit;
            }

            // 外注リードタイム（数量に依存しない営業日単位の待機）。
            // この工程の完了日からOutsourceLeadDays日分後ろ倒しし、後続工程を繰り越す
            if (sorted[i].OutsourceLeadDays > 0) {
                running = (chunks[i] + sorted[i].OutsourceLeadDays) * 480.0;
            }
        }
        // 最終工程にOutsourceLeadDaysがある場合、chunks.Max()には反映されないため
        // 最終的なrunning（総所要分数）から総チャンク数を算出する
        int totalChunks = Math.Max(1, (int)Math.Ceiling(running / 480.0));

        // 各工程の予定日 = 完了日 - (totalChunks - chunk) 営業日
        var results = new List<OrderProcess>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++) {
            var def = sorted[i];
            double requiredMinutes = (def.LeadTimeMinutes ?? 0) * order.PlannedQuantity;
            var dueDate = SubtractBusinessDays(order.CompletionDate, totalChunks - chunks[i]);
            // 480分超えの工程は複数日にまたがるため、開始日を別途計算する
            var daysSpan = requiredMinutes > 0 ? (int)Math.Ceiling(requiredMinutes / 480.0) - 1 : 0;
            var startDate = SubtractBusinessDays(dueDate, daysSpan);
            // 指示先番号（一意）で完了判定。指示内容（表示名）の重複の影響を受けない
            var isCompleted = completedByDestNumber.TryGetValue(def.CsvColumnName, out var actualDate);
            results.Add(new OrderProcess {
                ProcessName = def.ProcessName,
                StartDate = startDate,
                DueDate = dueDate,
                ActualDate = isCompleted ? actualDate : null,
                Status = isCompleted ? ProcessStatus.Completed : ProcessStatus.NotStarted,
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
