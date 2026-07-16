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
        var calculator = new BusinessDayCalculator(holidays: [], dayMinutes: 480);
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
        var calculator = new BusinessDayCalculator(holidays: [], dayMinutes: 480);
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

    /// <summary>
    /// 外注待ちが2回連続する場合、2回目（より上流側）の外注ゲートは、1回目の外注待ちで
    /// 生じた丸め分の繰り越しも引き継いだ上で計算されなければならない。
    /// 引き継ぎが正しければ、Aの完了日とBの着手日の間にAの外注待ち日数(2営業日)分の
    /// 空白がそのまま現れる。
    /// </summary>
    [Fact]
    public void BuildProcesses_TwoConsecutiveOutsourceWaits_ShouldCarryOverRoundingToEarlierGate() {
        var calculator = new BusinessDayCalculator(holidays: [], dayMinutes: 480);
        var order = MakeOrder(new DateOnly(2026, 6, 30));

        var defs = new[] {
            Def(sortOrder: 1, setupMinutes: 30, destCode: "A", outsourceLeadDays: 2),
            Def(sortOrder: 2, setupMinutes: 50, destCode: "B", outsourceLeadDays: 2),
            Def(sortOrder: 3, setupMinutes: 100, destCode: "C"),
        };

        var processes = calculator.BuildProcesses(order, defs, completedByDestNumber: []);
        var a = processes.Single(p => p.DestinationCode == "A");
        var b = processes.Single(p => p.DestinationCode == "B");

        // B: Cの100分が1日に丸められた後、外注待ち2日分前倒しされ、3営業日前の1日に収まる
        var expectedBDay = calculator.SubtractBusinessDays(order.CompletionDate, 3);
        Assert.Equal(expectedBDay, b.DueDate);
        Assert.Equal(expectedBDay, b.StartDate);

        // A: Bのゲート(1490分=3営業日強)を丸めた後、さらに外注待ち2日分前倒しされ、
        // 6営業日前の1日に収まる（Bとの間に丸めた2営業日分の空白が生じる）
        var expectedADay = calculator.SubtractBusinessDays(order.CompletionDate, 6);
        Assert.Equal(expectedADay, a.DueDate);
        Assert.Equal(expectedADay, a.StartDate);
    }

    /// <summary>
    /// 後続に所要時間0分の工程（外注待ちあり）が存在する場合、adjustedがちょうど480の倍数になり、
    /// daysSoFarの境界判定を誤ると外注バッファが不要に1日分過大評価されてしまう。
    /// </summary>
    [Fact]
    public void BuildProcesses_ZeroLeadTimeProcessWithOutsourceWait_ShouldNotOverestimateBuffer() {
        var calculator = new BusinessDayCalculator(holidays: [], dayMinutes: 480);
        var order = MakeOrder(new DateOnly(2026, 6, 30));

        var defs = new[] {
            Def(sortOrder: 1, setupMinutes: 30, destCode: "A", outsourceLeadDays: 2),
            Def(sortOrder: 2, setupMinutes: 0, destCode: "B", outsourceLeadDays: 2), // 0分
            Def(sortOrder: 3, setupMinutes: 100, destCode: "C"),
        };

        var processes = calculator.BuildProcesses(order, defs, completedByDestNumber: []);
        var a = processes.Single(p => p.DestinationCode == "A");

        // Bが0分のため、Aの完了日は5営業日前（過大評価されると6営業日前になってしまう）
        var expectedADay = calculator.SubtractBusinessDays(order.CompletionDate, 5);
        Assert.Equal(expectedADay, a.DueDate);
    }

    /// <summary>
    /// PlannedQuantityが1より大きい場合、LeadTimeMinutes×数量で実際の所要時間が決まる。
    /// 数量倍率を反映した所要時間でも、外注待ちゲートとの繰り越し計算が正しく動くことを確認する
    /// （単価30分×10個=300分のAと外注待ちのBの組み合わせは、単一テストの300分のAと同じ結果になるはず）。
    /// </summary>
    [Fact]
    public void BuildProcesses_PlannedQuantityGreaterThanOne_ShouldScaleMinutesBeforeGateCalculation() {
        var calculator = new BusinessDayCalculator(holidays: [], dayMinutes: 480);
        var order = MakeOrder(new DateOnly(2026, 6, 30), plannedQuantity: 10);

        var defs = new[] {
            Def(sortOrder: 1, setupMinutes: 30, destCode: "A"),  // ×10個=300分
            Def(sortOrder: 2, setupMinutes: 20, destCode: "B", outsourceLeadDays: 2), // ×10個=200分
            Def(sortOrder: 3, setupMinutes: 10, destCode: "C"),  // ×10個=100分
        };

        var processes = calculator.BuildProcesses(order, defs, completedByDestNumber: []);
        var a = processes.Single(p => p.DestinationCode == "A");
        var b = processes.Single(p => p.DestinationCode == "B");

        var expectedBDay = calculator.SubtractBusinessDays(order.CompletionDate, 3);
        Assert.Equal(expectedBDay, b.DueDate);

        // AはBと同じ日に完了でき、着手はその1営業日前
        Assert.Equal(expectedBDay, a.DueDate);
        Assert.Equal(calculator.SubtractBusinessDays(order.CompletionDate, 4), a.StartDate);
    }

    /// <summary>
    /// 外注待ちがなくても、滞留時間(DwellTimeMinutes)が480分を超えると単独で営業日を
    /// またいで繰り越されることを確認する。
    /// </summary>
    [Fact]
    public void BuildProcesses_DwellTimeOnly_ShouldCarryOverBusinessDayWithoutOutsource() {
        var calculator = new BusinessDayCalculator(holidays: [], dayMinutes: 480);
        var order = MakeOrder(new DateOnly(2026, 6, 30));

        var defs = new[] {
            Def(sortOrder: 1, setupMinutes: 30, destCode: "A"),
            Def(sortOrder: 2, setupMinutes: 50, destCode: "B", dwellMinutes: 600), // 滞留600分
            Def(sortOrder: 3, setupMinutes: 100, destCode: "C"),
        };

        var processes = calculator.BuildProcesses(order, defs, completedByDestNumber: []);
        var a = processes.Single(p => p.DestinationCode == "A");
        var b = processes.Single(p => p.DestinationCode == "B");
        var c = processes.Single(p => p.DestinationCode == "C");

        // Cは完了日当日（100分のみ）
        Assert.Equal(order.CompletionDate, c.DueDate);

        // Bは滞留600分が加わることで、Cと同じ日には収まらず1営業日前に繰り越る
        var expectedBDay = calculator.SubtractBusinessDays(order.CompletionDate, 1);
        Assert.Equal(expectedBDay, b.DueDate);

        // AはBと同じ日に収まる（Bの所要時間+滞留時間がまだ480分の枠に余裕を残すため）
        Assert.Equal(expectedBDay, a.DueDate);
    }
}
