using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Tests;

public class BusinessDayCalculatorTests
{
    private static ProcessDefinition Def(int sortOrder, double setupMinutes, string destCode, int outsourceLeadDays = 0, double dwellMinutes = 0) => new() {
        SortOrder = sortOrder,
        SetupTimeMinutes = setupMinutes,
        WorkTimeMinutes = 0,
        DestinationCode = destCode,
        OutsourceLeadDays = outsourceLeadDays,
        DwellTimeMinutes = dwellMinutes,
        IsVisible = true
    };

    private static Order MakeOrder(DateOnly completionDate, int plannedQuantity = 1) => new() {
        CompletionDate = completionDate,
        PlannedQuantity = plannedQuantity
    };

    /// <summary>
    /// 外注待ちが工程Bにある場合、Bの所要時間(200分)は分単位で正確に計算され、
    /// 480分に満たない余り(280分)は前工程Aが同じ日を使えるようになる。
    /// そのため、AのDueDateとBのDueDateは同じ日になり、Aだけが1日前から着手する。
    /// </summary>
    [Fact]
    public void BuildProcesses_OutsourceWaitOnMiddleProcess_ShouldShareRemainingCapacityWithPrecedingProcess() {
        var calculator = new BusinessDayCalculator(holidays: []);
        var order = MakeOrder(new DateOnly(2026, 6, 30)); // 火曜日

        var defs = new[] {
            Def(sortOrder: 1, setupMinutes: 300, destCode: "A"),
            Def(sortOrder: 2, setupMinutes: 200, destCode: "B", outsourceLeadDays: 2),
            Def(sortOrder: 3, setupMinutes: 100, destCode: "C"),
        };

        var processes = calculator.BuildProcesses(order, defs, completedByDestNumber: []);
        var a = processes.Single(p => p.DestinationCode == "A");
        var b = processes.Single(p => p.DestinationCode == "B");
        var c = processes.Single(p => p.DestinationCode == "C");

        // Cは完了日当日に収まる（100分のみ）
        Assert.Equal(order.CompletionDate, c.DueDate);
        Assert.Equal(order.CompletionDate, c.StartDate);

        // Bは外注待ち2営業日分前倒しされ、当日中に収まる（200分のみ）
        var expectedBDay = calculator.SubtractBusinessDays(order.CompletionDate, 3);
        Assert.Equal(expectedBDay, b.DueDate);
        Assert.Equal(expectedBDay, b.StartDate);

        // AはBと同じ日に完了でき（Bの余り280分を使う）、着手はその1営業日前
        Assert.Equal(expectedBDay, a.DueDate);
        Assert.Equal(calculator.SubtractBusinessDays(order.CompletionDate, 4), a.StartDate);
    }

    /// <summary>
    /// 外注待ちがない場合、3工程の合計所要時間(600分)は480分を1つ超えるだけなので、
    /// 全工程が完了日当日〜前営業日の2日間に収まる。
    /// </summary>
    [Fact]
    public void BuildProcesses_NoOutsourceWait_ShouldPackProcessesTightly() {
        var calculator = new BusinessDayCalculator(holidays: []);
        var order = MakeOrder(new DateOnly(2026, 6, 30));

        var defs = new[] {
            Def(sortOrder: 1, setupMinutes: 300, destCode: "A"),
            Def(sortOrder: 2, setupMinutes: 200, destCode: "B"),
            Def(sortOrder: 3, setupMinutes: 100, destCode: "C"),
        };

        var processes = calculator.BuildProcesses(order, defs, completedByDestNumber: []);
        var a = processes.Single(p => p.DestinationCode == "A");
        var b = processes.Single(p => p.DestinationCode == "B");
        var c = processes.Single(p => p.DestinationCode == "C");

        Assert.Equal(order.CompletionDate, c.DueDate);
        Assert.Equal(order.CompletionDate, b.DueDate);
        Assert.Equal(order.CompletionDate, a.DueDate);
        Assert.Equal(calculator.SubtractBusinessDays(order.CompletionDate, 1), a.StartDate);
    }
}
