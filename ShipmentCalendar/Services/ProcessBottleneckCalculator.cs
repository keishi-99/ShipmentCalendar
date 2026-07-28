using ShipmentCalendar.Models;

namespace ShipmentCalendar.Services;

/// <summary>期間内の完了実績（品目横断）から、品目×工程別の標準時間超過の傾向を集計する。
/// 同じ工程名（取引先）でも品目によって作業内容が異なるため、集計は品目番号と工程名の組み合わせ単位で行う。</summary>
public static class ProcessBottleneckCalculator {
    public static List<ProcessBottleneckRow> Aggregate(
        IEnumerable<(string Seiban, string ItemNumber, string ItemName, string ModelCode, string DestinationCode, DateOnly ActualDate, string WorkerName, double ActualWorkMinutes, int PlannedQuantity, DateOnly? DeliveryDate)> completedRows,
        IDictionary<string, List<ProcessDefinition>> defsByItemNumber) {

        var processResults = new List<(string ItemNumber, string ItemName, string ProcessName, string Seiban, double RequiredMinutes, double ActualWorkMinutes)>();

        foreach (var group in completedRows.GroupBy(r => r.Seiban, StringComparer.OrdinalIgnoreCase)) {
            var first = group.First();
            if (!defsByItemNumber.TryGetValue(first.ItemNumber, out var defs) || defs.Count == 0) continue;
            var defByDest = defs.DistinctBy(d => d.DestinationCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(d => d.DestinationCode, StringComparer.OrdinalIgnoreCase);

            // 同一工程（指示先番号）に複数の受入実績がある場合は作業時間を合計する
            foreach (var destGroup in group.GroupBy(r => r.DestinationCode, StringComparer.OrdinalIgnoreCase)) {
                if (!defByDest.TryGetValue(destGroup.Key, out var def)) continue;

                var actualWorkMinutes = destGroup.Sum(r => r.ActualWorkMinutes);
                var requiredMinutes = def.LeadTimeMinutes * first.PlannedQuantity;
                processResults.Add((first.ItemNumber, first.ItemName, def.ProcessName, first.Seiban, requiredMinutes, actualWorkMinutes));
            }
        }

        return processResults
            .GroupBy(x => (x.ItemNumber, x.ProcessName), StringPairComparer.OrdinalIgnoreCase)
            .Select(g => {
                var count = g.Count();
                var overStandard = g.Where(x => x.RequiredMinutes > 0 && x.ActualWorkMinutes > x.RequiredMinutes).ToList();
                return new ProcessBottleneckRow {
                    ItemNumber = g.Key.ItemNumber,
                    ItemName = g.First().ItemName,
                    ProcessName = g.Key.ProcessName,
                    CompletedCount = count,
                    OverStandardCount = overStandard.Count,
                    AvgOverMinutes = overStandard.Count > 0 ? overStandard.Average(x => x.ActualWorkMinutes - x.RequiredMinutes) : 0,
                    // 超過分が大きい注文ほど先頭に来るようにし、外れ値をすぐ確認できるようにする
                    Items = g.OrderByDescending(x => x.ActualWorkMinutes - x.RequiredMinutes)
                        .Select(x => new ProcessBottleneckItem {
                            Seiban = x.Seiban,
                            RequiredMinutes = x.RequiredMinutes,
                            ActualWorkMinutes = x.ActualWorkMinutes
                        }).ToList()
                };
            })
            .OrderByDescending(r => r.OverStandardRate)
            .ThenByDescending(r => r.CompletedCount)
            .ToList();
    }

    /// <summary>(ItemNumber, ProcessName)キーの品目番号部分を大文字小文字区別なしで比較する。
    /// ProcessNameは取引先名称そのものでキーとして揺れにくいため、ItemNumberのみ大小無視で十分</summary>
    private class StringPairComparer : IEqualityComparer<(string ItemNumber, string ProcessName)> {
        public static readonly StringPairComparer OrdinalIgnoreCase = new();

        public bool Equals((string ItemNumber, string ProcessName) x, (string ItemNumber, string ProcessName) y) =>
            string.Equals(x.ItemNumber, y.ItemNumber, StringComparison.OrdinalIgnoreCase) && x.ProcessName == y.ProcessName;

        public int GetHashCode((string ItemNumber, string ProcessName) obj) =>
            HashCode.Combine(obj.ItemNumber.ToUpperInvariant(), obj.ProcessName);
    }
}
