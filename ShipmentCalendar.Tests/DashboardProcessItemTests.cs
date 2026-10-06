using ShipmentCalendar.Models;

namespace ShipmentCalendar.Tests;

public class DashboardProcessItemTests {
    private static DashboardProcessItem MakeItem(ProcessStatus status, DateOnly dueDate, DateOnly? actualDate = null)
        => new() {
            Order = new Order(),
            Process = new OrderProcess { Status = status, DueDate = dueDate, ActualDate = actualDate },
        };

    // お手本: 未完了の工程は「期限 M/d」と表示される
    [Fact]
    public void DateText_NotCompleted_ShowsDueDate() {
        var item = MakeItem(ProcessStatus.InProgress, dueDate: new DateOnly(2026, 6, 20));

        Assert.Equal("期限 6/20", item.DateText);
    }

    // 完了済みで ActualDate がある工程は「完了 M/d」と表示される
    [Fact]
    public void DateText_Completed_ShowsActualDate() {
        var item = MakeItem(ProcessStatus.Completed, dueDate: new DateOnly(2026, 6, 20), actualDate: new DateOnly(2026, 6, 10));

        Assert.Equal("完了 6/10", item.DateText);
    }

    // ステータスが完了でも ActualDate が null の工程は「期限 M/d」と表示される
    [Fact]
    public void DateText_CompletedWithoutActualDate_ShowsDueDate() {
        var item = MakeItem(ProcessStatus.Completed, dueDate: new DateOnly(2026, 6, 20), actualDate: null);
        Assert.Equal("期限 6/20", item.DateText);
    }
}
