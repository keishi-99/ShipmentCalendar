using ShipmentCalendar.Models;

namespace ShipmentCalendar.Services;

/// <summary>部署別・日別の締切集中度を集計する。
/// 表示中の注文の未完了工程について、各工程のRequiredMinutesをStartDate〜DueDateの営業日へ
/// 按分したDailyMinutes（BusinessDayCalculator.BuildProcessesで算出）を日ごとに合算し、
/// 「その日、部署にどれだけの作業時間が発生するか」を示す。実際の着手タイミングは現場判断のため、
/// これは実績や確定スケジュールではなく、締切に基づくリスクの目安である。
/// 部署に基本人数（Headcount）が設定されている場合、その日の実働人数（Headcount−その日の欠員数、0未満は0）を求め、
/// 充足率＝合計必要時間÷(実働人数×1日の稼働時間)で「やや集中／集中」を判定する。
/// Headcount未設定、または実働人数が0の部署・日は判定できないため常にNormalとする。</summary>
public static class DepartmentLoadCalculator {
    public static List<DepartmentLoadRow> Aggregate(
        IEnumerable<Order> orders, IEnumerable<Department> departments, IEnumerable<DepartmentAbsence> absences,
        double dayMinutes, double cautionPercent, double concentratedPercent) {
        var absenceMap = absences.ToDictionary(a => (a.DepartmentId, a.Date), a => a.AbsentCount);
        var grouped = orders
            .SelectMany(o => o.Processes.Select(p => (Order: o, Process: p)))
            .Where(x => x.Process.Status != ProcessStatus.Completed)
            .SelectMany(x => x.Process.DailyMinutes.Select(dm => (x.Order, x.Process, Date: dm.Key, DayMinutes: dm.Value)))
            .GroupBy(x => (x.Process.DepartmentId, x.Date))
            .ToDictionary(g => g.Key, g => (
                Count: g.Select(x => x.Process).Distinct().Count(),
                Minutes: g.Sum(x => x.DayMinutes),
                Items: g.Select(x => new DepartmentLoadCellItem { Order = x.Order, Process = x.Process, DayMinutes = x.DayMinutes }).ToList()));

        if (grouped.Count == 0) return [];

        var minDate = grouped.Keys.Min(k => k.Date);
        var maxDate = grouped.Keys.Max(k => k.Date);
        var dates = new List<DateOnly>();
        for (var d = minDate; d <= maxDate; d = d.AddDays(1))
            dates.Add(d);

        // マスタに存在する部署に加え、部署未設定（DepartmentId=0）の工程がある場合は「未設定」行を追加する
        var departmentEntries = departments.Select(d => (Id: d.Id, Name: d.Name, Headcount: d.Headcount)).ToList();
        if (grouped.Keys.Any(k => k.DepartmentId == 0))
            departmentEntries.Add((0, "未設定", 0));

        var rows = new List<DepartmentLoadRow>();
        foreach (var (id, name, headcount) in departmentEntries) {
            var cells = dates.Select(date => {
                grouped.TryGetValue((id, date), out var agg);
                var absentCount = absenceMap.GetValueOrDefault((id, date), 0);
                var activeHeadcount = Math.Max(0, headcount - absentCount);
                var capacity = activeHeadcount * dayMinutes;
                var fulfillmentPercent = capacity > 0 ? agg.Minutes / capacity * 100 : (double?)null;
                return new DepartmentLoadCell {
                    Date = date,
                    ProcessCount = agg.Count,
                    TotalMinutes = agg.Minutes,
                    Items = agg.Items ?? [],
                    FulfillmentPercent = fulfillmentPercent,
                    AbsentCount = absentCount,
                    Level = DetermineCongestionLevel(fulfillmentPercent, cautionPercent, concentratedPercent)
                };
            }).ToList();
            rows.Add(new DepartmentLoadRow { DepartmentId = id, DepartmentName = name, Headcount = headcount, Cells = cells });
        }
        return rows;
    }

    /// <summary>充足率から、その部署・その日の締切集中度を判定する。
    /// 充足率が算出できない（基本人数が未設定）場合はNormal固定とする</summary>
    private static CongestionLevel DetermineCongestionLevel(double? fulfillmentPercent, double cautionPercent, double concentratedPercent) {
        if (fulfillmentPercent is not { } percent || percent <= 0) return CongestionLevel.Normal;
        if (percent >= concentratedPercent) return CongestionLevel.Concentrated;
        if (percent >= cautionPercent) return CongestionLevel.Caution;
        return CongestionLevel.Normal;
    }
}
