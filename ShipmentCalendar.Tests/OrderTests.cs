using ShipmentCalendar.Models;

namespace ShipmentCalendar.Tests;

public class OrderTests {
    // 指定したステータスの工程を持つ注文を作る（params: 引数を可変個で渡せる C# の構文）
    private static Order MakeOrder(params ProcessStatus[] statuses)
        => new() {
            Processes = statuses.Select(s => new OrderProcess { Status = s }).ToList(),
        };

    [Fact]
    public void IsAllCompleted_NoProcesses_ReturnsFalse() {
        var order = MakeOrder();

        Assert.False(order.IsAllCompleted);
    }

    [Fact]
    public void IsAllCompleted_AllCompleted_ReturnsTrue() {
        var order = MakeOrder(ProcessStatus.Completed, ProcessStatus.Completed);

        Assert.True(order.IsAllCompleted);
    }

    [Fact]
    public void IsAllCompleted_SomeNotCompleted_ReturnsFalse() {
        var order = MakeOrder(ProcessStatus.Completed, ProcessStatus.InProgress);

        Assert.False(order.IsAllCompleted);
    }

    [Fact]
    public void HasOverdue_SomeOverdue_ReturnsTrue() {
        var order = MakeOrder(ProcessStatus.Completed, ProcessStatus.Overdue);
        Assert.True(order.HasOverdue);
    }

    [Fact]
    public void HasOverdue_NoOverdue_ReturnsFalse() {
        var order = MakeOrder(ProcessStatus.Completed, ProcessStatus.InProgress);
        Assert.False(order.HasOverdue);
    }

    [Fact]
    public void HasOverdue_NoProcesses_ReturnsFalse() {
        var order = MakeOrder();
        Assert.False(order.HasOverdue);
    }

    [Fact]
    public void HasWarning_SomeWarning_ReturnsTrue() {
        var order = MakeOrder(ProcessStatus.Completed, ProcessStatus.Warning);
        Assert.True(order.HasWarning);
    }

    [Fact]
    public void HasWarning_NoWarning_ReturnsFalse() {
        var order = MakeOrder(ProcessStatus.Completed, ProcessStatus.InProgress);
        Assert.False(order.HasWarning);
    }

    [Fact]
    public void HasWarning_NoProcesses_ReturnsFalse() {
        var order = MakeOrder();
        Assert.False(order.HasWarning);
    }
}
