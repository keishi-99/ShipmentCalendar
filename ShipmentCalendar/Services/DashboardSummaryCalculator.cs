using ShipmentCalendar.Models;

namespace ShipmentCalendar.Services;

/// <summary>受注全体の進捗状況を、全体件数・完了率・超過/警告件数、および部署別の残工程数・超過件数で集計する</summary>
public static class DashboardSummaryCalculator
{
    public static DashboardSummary Aggregate(IEnumerable<Order> orders, IEnumerable<Department> departments)
    {
        var orderList = orders.ToList();
        var processEntries = orderList.SelectMany(o => o.Processes.Select(p => (Order: o, Process: p))).ToList();

        // マスタに存在する部署に加え、部署未設定（DepartmentId=0）の工程がある場合は「未設定」行を追加する
        var departmentEntries = departments.Select(d => (d.Id, d.Name)).ToList();
        if (processEntries.Any(x => x.Process.DepartmentId == 0))
            departmentEntries.Add((0, "未設定"));

        var departmentRows = departmentEntries
            .Select(d => {
                var deptEntries = processEntries.Where(x => x.Process.DepartmentId == d.Id).ToList();
                var completed = deptEntries.Where(x => x.Process.Status == ProcessStatus.Completed).ToList();
                var remaining = deptEntries.Where(x => x.Process.Status != ProcessStatus.Completed).ToList();
                var overdue = remaining.Where(x => x.Process.Status == ProcessStatus.Overdue).ToList();

                return new DashboardDepartmentRow {
                    DepartmentId = d.Id,
                    DepartmentName = d.Name,
                    TotalProcessCount = deptEntries.Count,
                    CompletedProcessCount = completed.Count,
                    RemainingProcessCount = remaining.Count,
                    OverdueProcessCount = overdue.Count,
                    AllItems = deptEntries
                        // 超過→未完了→完了の順で並べ、対応が必要なものほど先頭に来るようにする
                        .OrderBy(x => x.Process.Status == ProcessStatus.Completed)
                        .ThenByDescending(x => x.Process.Status == ProcessStatus.Overdue)
                        .ThenBy(x => x.Process.DueDate)
                        .Select(x => new DashboardProcessItem { Order = x.Order, Process = x.Process })
                        .ToList(),
                    // 直近に完了したものほど先頭に来るようにし、最新の実績から確認できるようにする
                    CompletedItems = completed
                        .OrderByDescending(x => x.Process.ActualDate)
                        .Select(x => new DashboardProcessItem { Order = x.Order, Process = x.Process })
                        .ToList(),
                    // 超過中のものを先頭にし、その中では締切が古い順に並べて対応の優先度がわかるようにする
                    RemainingItems = remaining
                        .OrderByDescending(x => x.Process.Status == ProcessStatus.Overdue)
                        .ThenBy(x => x.Process.DueDate)
                        .Select(x => new DashboardProcessItem { Order = x.Order, Process = x.Process })
                        .ToList(),
                    OverdueItems = overdue
                        .OrderBy(x => x.Process.DueDate)
                        .Select(x => new DashboardProcessItem { Order = x.Order, Process = x.Process })
                        .ToList(),
                };
            })
            // 対象工程が1件もない部署は一覧に出しても意味がないため除外する
            .Where(r => r.TotalProcessCount > 0)
            .OrderByDescending(r => r.OverdueProcessCount)
            .ThenByDescending(r => r.RemainingProcessCount)
            .ToList();

        return new DashboardSummary {
            TotalCount = orderList.Count,
            CompletedCount = orderList.Count(o => o.IsAllCompleted),
            OverdueCount = orderList.Count(o => o.HasOverdue),
            WarningCount = orderList.Count(o => o.HasWarning),
            DepartmentRows = departmentRows,
        };
    }
}
