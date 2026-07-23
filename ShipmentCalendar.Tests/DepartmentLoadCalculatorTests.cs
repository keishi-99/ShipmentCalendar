using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Tests;

public class DepartmentLoadCalculatorTests
{
    private static readonly Department[] NoDepartments = [];

    private static OrderProcess MakeProcess(int departmentId, DateOnly dueDate, double requiredMinutes, ProcessStatus status = ProcessStatus.NotStarted, string destinationCode = "P1") => new() {
        DepartmentId = departmentId,
        DueDate = dueDate,
        RequiredMinutes = requiredMinutes,
        Status = status,
        DestinationCode = destinationCode,
        ProcessName = destinationCode
    };

    private static Order MakeOrder(string manufactureNumber, params OrderProcess[] processes) => new() {
        ManufactureNumber = manufactureNumber,
        Processes = [.. processes]
    };

    [Fact]
    public void Aggregate_NoOrders_ReturnsEmptyList() {
        var rows = DepartmentLoadCalculator.Aggregate([], NoDepartments, cautionMinutes: 500, concentratedMinutes: 1000);

        Assert.Empty(rows);
    }

    [Fact]
    public void Aggregate_AllProcessesCompleted_ReturnsEmptyList() {
        var order = MakeOrder("M1", MakeProcess(1, new DateOnly(2026, 6, 30), 100, ProcessStatus.Completed));

        var rows = DepartmentLoadCalculator.Aggregate([order], NoDepartments, cautionMinutes: 500, concentratedMinutes: 1000);

        Assert.Empty(rows);
    }

    [Fact]
    public void Aggregate_SameDepartmentAndDate_SumsCountAndMinutes() {
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1",
            MakeProcess(1, dueDate, 100, destinationCode: "P1"),
            MakeProcess(1, dueDate, 150, destinationCode: "P2"));
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = DepartmentLoadCalculator.Aggregate([order], departments, cautionMinutes: 500, concentratedMinutes: 1000);

        var cell = rows.Single(r => r.DepartmentName == "総務部").Cells.Single(c => c.Date == dueDate);
        Assert.Equal(2, cell.ProcessCount);
        Assert.Equal(250, cell.TotalMinutes);
    }

    [Fact]
    public void Aggregate_UnknownDepartmentId_AddsFallbackRow() {
        var order = MakeOrder("M1", MakeProcess(departmentId: 0, new DateOnly(2026, 6, 30), 100));

        var rows = DepartmentLoadCalculator.Aggregate([order], NoDepartments, cautionMinutes: 500, concentratedMinutes: 1000);

        Assert.Contains(rows, r => r.DepartmentName == "未設定");
    }

    [Fact]
    public void Aggregate_DepartmentWithNoData_StillAppearsWithEmptyCells() {
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, 100));
        var departments = new[] {
            new Department { Id = 1, Name = "総務部" },
            new Department { Id = 2, Name = "製造部" },
        };

        var rows = DepartmentLoadCalculator.Aggregate([order], departments, cautionMinutes: 500, concentratedMinutes: 1000);

        var manufacturingRow = rows.Single(r => r.DepartmentName == "製造部");
        Assert.All(manufacturingRow.Cells, c => Assert.Equal(0, c.ProcessCount));
    }

    [Fact]
    public void Aggregate_DateRange_FillsEveryDateBetweenMinAndMaxDueDate() {
        var order = MakeOrder("M1",
            MakeProcess(1, new DateOnly(2026, 6, 30), 100, destinationCode: "P1"),
            MakeProcess(1, new DateOnly(2026, 7, 3), 100, destinationCode: "P2"));
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = DepartmentLoadCalculator.Aggregate([order], departments, cautionMinutes: 500, concentratedMinutes: 1000);

        var dates = rows.Single().Cells.Select(c => c.Date).ToList();
        // 6/30〜7/3の4日分（休日も含めて連続で埋める。営業日判定はしない）
        Assert.Equal([new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 3)], dates);
    }

    [Theory]
    [InlineData(499, CongestionLevel.Normal)]
    [InlineData(500, CongestionLevel.Caution)]
    [InlineData(999, CongestionLevel.Caution)]
    [InlineData(1000, CongestionLevel.Concentrated)]
    public void Aggregate_ThresholdBoundaries_ClassifyCongestionLevel(double requiredMinutes, CongestionLevel expected) {
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, requiredMinutes));
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = DepartmentLoadCalculator.Aggregate([order], departments, cautionMinutes: 500, concentratedMinutes: 1000);

        var cell = rows.Single().Cells.Single(c => c.Date == dueDate);
        Assert.Equal(expected, cell.Level);
    }

    /// <summary>
    /// しきい値を0にすると、totalMinutes(0) &gt;= cautionMinutes(0)でCautionと誤判定されうる境界値。
    /// CodeRabbitの指摘で見つかった不具合の再発防止。
    /// </summary>
    [Fact]
    public void Aggregate_CautionThresholdIsZero_EmptyCellStaysNormal() {
        // 部署2に7/2の工程を混ぜて日付範囲を6/30〜7/2に広げ、部署1の7/1を「データなしの空セル」にする
        var order1 = MakeOrder("M1", MakeProcess(1, new DateOnly(2026, 6, 30), 100));
        var order2 = MakeOrder("M2", MakeProcess(2, new DateOnly(2026, 7, 2), 100));
        var departments = new[] { new Department { Id = 1, Name = "総務部" }, new Department { Id = 2, Name = "製造部" } };

        var rows = DepartmentLoadCalculator.Aggregate([order1, order2], departments, cautionMinutes: 0, concentratedMinutes: 1000);

        var emptyCell = rows.Single(r => r.DepartmentName == "総務部").Cells.Single(c => c.Date == new DateOnly(2026, 7, 1));
        Assert.Equal(0, emptyCell.ProcessCount);
        Assert.Equal(CongestionLevel.Normal, emptyCell.Level);
    }

    [Fact]
    public void Aggregate_Cell_CarriesOrderAndProcessForDrilldown() {
        var dueDate = new DateOnly(2026, 6, 30);
        var process = MakeProcess(1, dueDate, 100);
        var order = MakeOrder("M1", process);
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = DepartmentLoadCalculator.Aggregate([order], departments, cautionMinutes: 500, concentratedMinutes: 1000);

        var item = rows.Single().Cells.Single(c => c.Date == dueDate).Items.Single();
        Assert.Same(order, item.Order);
        Assert.Same(process, item.Process);
    }
}
