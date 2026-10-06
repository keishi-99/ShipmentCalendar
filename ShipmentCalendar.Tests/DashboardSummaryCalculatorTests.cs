using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Tests;

public class DashboardSummaryCalculatorTests {
    private static readonly DateOnly _baseDate = new(2026, 6, 15);

    private static OrderProcess Proc(int departmentId, ProcessStatus status, int dueOffsetDays = 0, DateOnly? actualDate = null, string name = "工程")
        => new() {
            ProcessName = name,
            DepartmentId = departmentId,
            Status = status,
            DueDate = _baseDate.AddDays(dueOffsetDays),
            StartDate = _baseDate.AddDays(dueOffsetDays),
            ActualDate = actualDate,
        };

    private static Order MakeOrder(string manufactureNumber, int deliveryOffsetDays, params OrderProcess[] processes)
        => new() {
            ManufactureNumber = manufactureNumber,
            DeliveryDate = _baseDate.AddDays(deliveryOffsetDays),
            Processes = [.. processes],
        };

    private static Department Dept(int id, string name) => new() { Id = id, Name = name };

    [Fact]
    public void Aggregate_NoOrders_ReturnsZeroCountsAndEmptyRows() {
        var summary = DashboardSummaryCalculator.Aggregate([], [Dept(1, "加工")]);

        Assert.Equal(0, summary.TotalCount);
        Assert.Equal(0, summary.TotalProcessCount);
        Assert.Empty(summary.DepartmentRows);
        Assert.Empty(summary.OrderRows);
    }

    [Fact]
    public void Aggregate_OrderAndProcessCounts_AreCountedByStatus() {
        var completedOrder = MakeOrder("A", 1, Proc(1, ProcessStatus.Completed));
        var overdueOrder = MakeOrder("B", 2, Proc(1, ProcessStatus.Overdue), Proc(1, ProcessStatus.Completed));
        var warningOrder = MakeOrder("C", 3, Proc(1, ProcessStatus.Warning), Proc(1, ProcessStatus.NotStarted));
        var noProcessOrder = MakeOrder("D", 4);

        var summary = DashboardSummaryCalculator.Aggregate([completedOrder, overdueOrder, warningOrder, noProcessOrder], [Dept(1, "加工")]);

        Assert.Equal(4, summary.TotalCount);
        Assert.Equal(1, summary.CompletedCount);
        Assert.Equal(1, summary.OverdueCount);
        Assert.Equal(1, summary.WarningCount);
        // 工程が0件の注文は「全完了」ではないので未完了に数える
        Assert.Equal(3, summary.IncompleteCount);
        Assert.Equal(5, summary.TotalProcessCount);
        Assert.Equal(2, summary.CompletedProcessCount);
        Assert.Equal(1, summary.OverdueProcessCount);
        Assert.Equal(1, summary.WarningProcessCount);
        Assert.Equal(3, summary.RemainingProcessCount);
    }

    [Fact]
    public void Aggregate_ProcessWithoutDepartment_AddsUnsetRow() {
        var order = MakeOrder("A", 1, Proc(0, ProcessStatus.NotStarted));

        var summary = DashboardSummaryCalculator.Aggregate([order], [Dept(1, "加工")]);

        var row = Assert.Single(summary.DepartmentRows);
        Assert.Equal(0, row.DepartmentId);
        Assert.Equal("未設定", row.DepartmentName);
    }

    [Fact]
    public void Aggregate_NoProcessWithoutDepartment_DoesNotAddUnsetRow() {
        var order = MakeOrder("A", 1, Proc(1, ProcessStatus.NotStarted));

        var summary = DashboardSummaryCalculator.Aggregate([order], [Dept(1, "加工")]);

        Assert.DoesNotContain(summary.DepartmentRows, r => r.DepartmentId == 0);
    }

    [Fact]
    public void Aggregate_DepartmentWithoutProcesses_IsExcludedFromRows() {
        var order = MakeOrder("A", 1, Proc(1, ProcessStatus.NotStarted));

        var summary = DashboardSummaryCalculator.Aggregate([order], [Dept(1, "加工"), Dept(2, "組立")]);

        var row = Assert.Single(summary.DepartmentRows);
        Assert.Equal(1, row.DepartmentId);
    }

    [Fact]
    public void Aggregate_DepartmentRows_SortedByOverdueThenRemainingDescending() {
        var order = MakeOrder("A", 1,
            Proc(1, ProcessStatus.NotStarted), Proc(1, ProcessStatus.NotStarted), Proc(1, ProcessStatus.NotStarted), // 部署1: 超過0・残3
            Proc(2, ProcessStatus.Overdue),                                                                         // 部署2: 超過1・残1
            Proc(3, ProcessStatus.NotStarted));                                                                     // 部署3: 超過0・残1

        var summary = DashboardSummaryCalculator.Aggregate([order], [Dept(1, "加工"), Dept(2, "組立"), Dept(3, "検査")]);

        Assert.Equal([2, 1, 3], summary.DepartmentRows.Select(r => r.DepartmentId));
    }

    [Fact]
    public void Aggregate_DepartmentRow_CountsAreSplitByStatus() {
        var order = MakeOrder("A", 1,
            Proc(1, ProcessStatus.Completed), Proc(1, ProcessStatus.Overdue), Proc(1, ProcessStatus.Warning));

        var row = Assert.Single(DashboardSummaryCalculator.Aggregate([order], [Dept(1, "加工")]).DepartmentRows);

        Assert.Equal(3, row.TotalProcessCount);
        Assert.Equal(1, row.CompletedProcessCount);
        Assert.Equal(2, row.RemainingProcessCount);
        Assert.Equal(1, row.OverdueProcessCount);
    }

    [Fact]
    public void Aggregate_DepartmentRow_AllItemsOrderedOverdueThenIncompleteThenCompleted() {
        var order = MakeOrder("A", 1,
            Proc(1, ProcessStatus.Completed, dueOffsetDays: 0, name: "完了"),
            Proc(1, ProcessStatus.NotStarted, dueOffsetDays: 5, name: "未着手"),
            Proc(1, ProcessStatus.Overdue, dueOffsetDays: 9, name: "超過"));

        var row = Assert.Single(DashboardSummaryCalculator.Aggregate([order], [Dept(1, "加工")]).DepartmentRows);

        Assert.Equal(["超過", "未着手", "完了"], row.AllItems.Select(i => i.ProcessName));
    }

    [Fact]
    public void Aggregate_DepartmentRow_CompletedItemsNewestActualDateFirst() {
        var order = MakeOrder("A", 1,
            Proc(1, ProcessStatus.Completed, actualDate: _baseDate.AddDays(-3), name: "古い"),
            Proc(1, ProcessStatus.Completed, actualDate: _baseDate.AddDays(-1), name: "新しい"));

        var row = Assert.Single(DashboardSummaryCalculator.Aggregate([order], [Dept(1, "加工")]).DepartmentRows);

        Assert.Equal(["新しい", "古い"], row.CompletedItems.Select(i => i.ProcessName));
    }

    [Fact]
    public void Aggregate_DepartmentRow_RemainingItemsOverdueFirstThenDueDateAscending() {
        var order = MakeOrder("A", 1,
            Proc(1, ProcessStatus.NotStarted, dueOffsetDays: 1, name: "未着手・早い"),
            Proc(1, ProcessStatus.Overdue, dueOffsetDays: 8, name: "超過・遅い"),
            Proc(1, ProcessStatus.NotStarted, dueOffsetDays: 3, name: "未着手・遅い"));

        var row = Assert.Single(DashboardSummaryCalculator.Aggregate([order], [Dept(1, "加工")]).DepartmentRows);

        Assert.Equal(["超過・遅い", "未着手・早い", "未着手・遅い"], row.RemainingItems.Select(i => i.ProcessName));
    }

    [Fact]
    public void Aggregate_DepartmentRow_OverdueItemsOldestDueDateFirst() {
        var order = MakeOrder("A", 1,
            Proc(1, ProcessStatus.Overdue, dueOffsetDays: -1, name: "新しい超過"),
            Proc(1, ProcessStatus.Overdue, dueOffsetDays: -5, name: "古い超過"));

        var row = Assert.Single(DashboardSummaryCalculator.Aggregate([order], [Dept(1, "加工")]).DepartmentRows);

        Assert.Equal(["古い超過", "新しい超過"], row.OverdueItems.Select(i => i.ProcessName));
    }

    [Fact]
    public void Aggregate_OrderWithoutProcesses_IsExcludedFromOrderRows() {
        var withProcess = MakeOrder("A", 1, Proc(1, ProcessStatus.NotStarted));
        var withoutProcess = MakeOrder("B", 2);

        var summary = DashboardSummaryCalculator.Aggregate([withProcess, withoutProcess], [Dept(1, "加工")]);

        var row = Assert.Single(summary.OrderRows);
        Assert.Equal("A", row.ManufactureNumber);
    }

    [Fact]
    public void Aggregate_OrderRows_SortedByOverdueCountDescendingThenDeliveryDateAscending() {
        var lateNoOverdue = MakeOrder("遅い納期・超過なし", 10, Proc(1, ProcessStatus.NotStarted));
        var earlyNoOverdue = MakeOrder("早い納期・超過なし", 2, Proc(1, ProcessStatus.NotStarted));
        var overdue = MakeOrder("超過あり", 20, Proc(1, ProcessStatus.Overdue));

        var summary = DashboardSummaryCalculator.Aggregate([lateNoOverdue, earlyNoOverdue, overdue], [Dept(1, "加工")]);

        Assert.Equal(["超過あり", "早い納期・超過なし", "遅い納期・超過なし"], summary.OrderRows.Select(r => r.ManufactureNumber));
    }

    [Fact]
    public void Aggregate_OrderRow_CountsAreSplitByStatus() {
        var order = MakeOrder("A", 1,
            Proc(1, ProcessStatus.Completed), Proc(1, ProcessStatus.Overdue), Proc(1, ProcessStatus.InProgress));

        var row = Assert.Single(DashboardSummaryCalculator.Aggregate([order], [Dept(1, "加工")]).OrderRows);

        Assert.Equal(3, row.TotalProcessCount);
        Assert.Equal(1, row.CompletedProcessCount);
        Assert.Equal(2, row.RemainingProcessCount);
        Assert.Equal(1, row.OverdueProcessCount);
    }
}
