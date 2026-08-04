using ShipmentCalendar.Models;

namespace ShipmentCalendar.Services;

/// <summary>ODBCから取得した受注データに、DBのユーザー設定を反映した工程定義をマージし、
/// 各注文のProcessesを構築してステータス確定・Overdue伝播まで行う（MainViewModel.LoadOrdersAsyncから抽出）</summary>
public static class OrderProcessBuildService {
    /// <summary>ordersを直接書き換える（ProductName・CompletionDate・Processes・各ProcessのStatus等）</summary>
    public static void Build(
        List<Order> orders,
        List<ProcessDefinition> allOdbcDefs,
        List<ProcessDefinition> dbDefs,
        IReadOnlyDictionary<string, string> displayNameOverrides,
        IReadOnlyDictionary<string, int> leadDaysOverrides,
        int defaultCompletionDateLeadDays,
        BusinessDayCalculator calculator,
        DateOnly today) {

        // DB登録済みの品目名があればODBC品目名を上書きする
        foreach (var order in orders) {
            if (displayNameOverrides.TryGetValue(order.ItemNumber, out var displayName) && !string.IsNullOrEmpty(displayName))
                order.ProductName = displayName;
        }

        // DB のユーザー設定（工程名カスタマイズ・LT・表示・警告）をマージ
        // キー: "ItemNumber|DestinationCode(=指示先番号)"
        var dbDict = dbDefs
            .Where(d => !string.IsNullOrEmpty(d.DestinationCode))
            .GroupBy(d => $"{d.ItemNumber}|{d.DestinationCode}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var allDefs = allOdbcDefs.Select(odbcDef => {
            var key = $"{odbcDef.ItemNumber}|{odbcDef.DestinationCode}";
            if (!dbDict.TryGetValue(key, out var db)) return odbcDef;
            return new ProcessDefinition {
                ItemNumber = odbcDef.ItemNumber,
                ProcessName = db.ProcessName,
                DestinationCode = odbcDef.DestinationCode,
                SortOrder = odbcDef.SortOrder,                           // 順序は常にODBC
                SetupTimeMinutes = db.SetupTimeMinutes,
                WorkTimeMinutes = db.WorkTimeMinutes,
                IsVisible = db.IsVisible,
                WarningDaysBeforeDeadline = db.WarningDaysBeforeDeadline,
                DepartmentId = db.DepartmentId,
                DwellTimeMinutes = db.DwellTimeMinutes,
                OutsourceLeadDays = db.OutsourceLeadDays
            };
        }).ToList();

        // 品目番号をキーにした工程定義辞書を構築（O(1)ルックアップ）
        var defDict = allDefs
            .GroupBy(d => d.ItemNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var order in orders) {
            defDict.TryGetValue(order.ItemNumber, out var productDefs);
            productDefs ??= [];

            var leadDays = leadDaysOverrides.TryGetValue(order.ItemNumber, out var itemLeadDays)
                ? itemLeadDays
                : defaultCompletionDateLeadDays;
            order.CompletionDate = calculator.SubtractBusinessDays(order.DeliveryDate, leadDays);

            if (productDefs.Count == 0)
                continue;

            // 仮登録した完了済み指示先番号→受入日・作業者名のマッピング（指示先番号は工程ごとに一意。重複は先着優先）
            var completedByDestNumber = order.Processes
                .Where(p => p.Status == ProcessStatus.Completed)
                .GroupBy(p => p.DestinationCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (g.First().ActualDate, g.First().WorkerName, g.First().ActualWorkMinutes), StringComparer.OrdinalIgnoreCase);

            order.Processes = calculator.BuildProcesses(order, productDefs.Where(d => d.IsVisible), completedByDestNumber);

            BusinessDayCalculator.MarkAllCompletedIfFinalReceiptDone(order.Processes, productDefs, completedByDestNumber);

            // ステータスを警告日数込みで確定
            foreach (var process in order.Processes) {
                var warningDays = productDefs
                    .FirstOrDefault(d => string.Equals(d.DestinationCode, process.DestinationCode, StringComparison.OrdinalIgnoreCase))
                    ?.WarningDaysBeforeDeadline ?? 0;
                process.WarningDaysBeforeDeadline = warningDays;
                process.Status = BusinessDayCalculator.DetermineStatus(process, today, warningDays);
            }

            // Overdue を後続工程に伝播（完了済みは除く）
            bool overdueFound = false;
            foreach (var process in order.Processes.OrderBy(p => p.SortOrder)) {
                if (process.Status == ProcessStatus.Overdue)
                    overdueFound = true;
                else if (overdueFound && process.Status != ProcessStatus.Completed)
                    process.Status = ProcessStatus.Overdue;
            }
        }
    }
}
