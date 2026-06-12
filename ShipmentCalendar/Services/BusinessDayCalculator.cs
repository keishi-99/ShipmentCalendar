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
    /// 末尾工程（完了日に近い工程）から逆向きに残り所要時間を累積し、480分（8時間）単位の
    /// 日チャンクに割り当てることで、各工程の「着手必須日（startBucket）」と
    /// 「完了必須日（finishBucket）」を別々に算出する。
    /// 工程自身の所要時間が日をまたぐ場合（例: 当日の残り枠に収まらず前営業日から
    /// 着手しないと完了できない場合）、着手必須日と完了必須日が異なる日になる。
    /// </summary>
    /// <summary>
    /// completedByDestNumber: 完了済み指示先番号→受入日 のマッピング（指示先番号は工程ごとに一意）
    /// </summary>
    public List<OrderProcess> BuildProcesses(Order order, IEnumerable<ProcessDefinition> definitions, Dictionary<string, DateOnly?> completedByDestNumber) {
        var sorted = definitions.OrderBy(d => d.SortOrder).ToList();
        if (!sorted.Any()) return new List<OrderProcess>();

        // 末尾工程から逆向きに、完了日から数えた日チャンク番号
        // （1=完了日当日、2=その前営業日…）で着手・完了それぞれの必須バケットを求める
        double runningIn = 0;
        double cumulativeRunningTime = 0;
        var startBucket = new int[sorted.Count];
        var finishBucket = new int[sorted.Count];
        for (int i = sorted.Count - 1; i >= 0; i--) {
            var def = sorted[i];
            double minutes = (def.LeadTimeMinutes ?? 0) * order.PlannedQuantity;
            double adjusted = runningIn;
            bool spansBoundary = minutes >= 480;

            // 外注リードタイム（数量に依存しない営業日単位の待機）。
            // この工程の後にOutsourceLeadDays日分の空白が入るため、その分だけ前倒しで締め切る
            if (def.OutsourceLeadDays > 0) {
                var daysSoFar = Math.Max(1, (int)Math.Ceiling(cumulativeRunningTime / 480.0));
                adjusted = (daysSoFar + def.OutsourceLeadDays) * 480.0;
                spansBoundary = true;
            }

            // クールタイム（数量に依存しない固定の待機時間）。外注リードタイムや末尾工程など
            // どのケースでも、adjustedの基準値に上乗せする。
            // 480分を超える分は、後段のceil計算により自動的に前営業日以前へ繰り越される
            if (def.CoolTimeMinutes > 0) {
                adjusted += def.CoolTimeMinutes;
            }

            if (spansBoundary) {
                // 外注待ち・480分超の工程は前後の工程と日をまたいで共有しないため、
                // 完了必須バケットは「後続工程群のバケットの次」から始まる
                finishBucket[i] = (int)Math.Ceiling(adjusted / 480.0) + 1;
                startBucket[i] = Math.Max(finishBucket[i], (int)Math.Ceiling(adjusted / 480.0) + (int)Math.Ceiling(minutes / 480.0));
                runningIn = startBucket[i] * 480.0;
            }
            else {
                finishBucket[i] = Math.Max(1, (int)Math.Ceiling(adjusted / 480.0));
                runningIn = adjusted + minutes;
                startBucket[i] = Math.Max(1, (int)Math.Ceiling(runningIn / 480.0));
            }

            cumulativeRunningTime += minutes + (def.OutsourceLeadDays * 480.0) + def.CoolTimeMinutes;
        }

        // 各工程の必須日 = 完了日 - (バケット番号 - 1) 営業日（バケット1=完了日当日）
        var results = new List<OrderProcess>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++) {
            var def = sorted[i];
            double requiredMinutes = (def.LeadTimeMinutes ?? 0) * order.PlannedQuantity;
            var dueDate = SubtractBusinessDays(order.CompletionDate, finishBucket[i] - 1);
            var startDate = SubtractBusinessDays(order.CompletionDate, startBucket[i] - 1);
            // 指示先番号（一意）で完了判定。指示内容（表示名）の重複の影響を受けない
            var isCompleted = completedByDestNumber.TryGetValue(def.DestinationCode, out var actualDate);
            results.Add(new OrderProcess {
                ProcessName = def.ProcessName,
                DestinationCode = def.DestinationCode,
                StartDate = startDate,
                DueDate = dueDate,
                ActualDate = isCompleted ? actualDate : null,
                Status = isCompleted ? ProcessStatus.Completed : ProcessStatus.NotStarted,
                SortOrder = def.SortOrder,
                DepartmentId = def.DepartmentId,
                RequiredMinutes = requiredMinutes,
                OutsourceLeadDays = def.OutsourceLeadDays
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
