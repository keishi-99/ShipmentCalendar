using ShipmentCalendar.Models;

namespace ShipmentCalendar.Services;

/// <summary>部署別・日別の締切集中度を集計する。
/// 表示中の注文の未完了工程についてDueDate（完了必須日）で集計し、「前倒し着手をしなかった場合に
/// その日どれだけの作業が締切を迎えるか」を示す。実際の着手タイミングは現場判断のため、
/// これは実績や確定スケジュールではなく、締切に基づくリスクの目安である。</summary>
public static class DepartmentLoadCalculator {
    public static List<DepartmentLoadRow> Aggregate(
        IEnumerable<Order> orders, IEnumerable<Department> departments,
        double cautionMinutes, double concentratedMinutes) {
        var grouped = orders
            .SelectMany(o => o.Processes)
            .Where(p => p.Status != ProcessStatus.Completed)
            .GroupBy(p => (p.DepartmentId, p.DueDate))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Minutes: g.Sum(p => p.RequiredMinutes)));

        if (grouped.Count == 0) return [];

        var minDate = grouped.Keys.Min(k => k.DueDate);
        var maxDate = grouped.Keys.Max(k => k.DueDate);
        var dates = new List<DateOnly>();
        for (var d = minDate; d <= maxDate; d = d.AddDays(1))
            dates.Add(d);

        // マスタに存在する部署に加え、部署未設定（DepartmentId=0）の工程がある場合は「未設定」行を追加する
        var departmentEntries = departments.Select(d => (Id: d.Id, Name: d.Name)).ToList();
        if (grouped.Keys.Any(k => k.DepartmentId == 0))
            departmentEntries.Add((0, "未設定"));

        var rows = new List<DepartmentLoadRow>();
        foreach (var (id, name) in departmentEntries) {
            var cells = dates.Select(date => {
                grouped.TryGetValue((id, date), out var agg);
                return new DepartmentLoadCell {
                    Date = date,
                    ProcessCount = agg.Count,
                    TotalMinutes = agg.Minutes,
                    Level = DetermineCongestionLevel(agg.Minutes, cautionMinutes, concentratedMinutes)
                };
            }).ToList();
            rows.Add(new DepartmentLoadRow { DepartmentName = name, Cells = cells });
        }
        return rows;
    }

    /// <summary>合計必要時間から、その部署・その日の締切集中度を判定する。
    /// 部署のキャパシティ（人員・稼働時間）は流動的で固定値を持てないため、絶対的な「稼働率」ではなく
    /// 相対的な閾値で「やや集中／集中」を判定する。閾値はウィンドウ上で現場の肌感覚に合わせて調整できる</summary>
    private static CongestionLevel DetermineCongestionLevel(double totalMinutes, double cautionMinutes, double concentratedMinutes) {
        if (totalMinutes <= 0) return CongestionLevel.Normal;
        if (totalMinutes >= concentratedMinutes) return CongestionLevel.Concentrated;
        if (totalMinutes >= cautionMinutes) return CongestionLevel.Caution;
        return CongestionLevel.Normal;
    }
}
