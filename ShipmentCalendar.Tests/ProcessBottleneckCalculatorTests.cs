using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Tests;

public class ProcessBottleneckCalculatorTests
{
    private static (string Seiban, string ItemNumber, string ItemName, string ModelCode, string DestinationCode, DateOnly ActualDate, string WorkerName, double ActualWorkMinutes, int PlannedQuantity, DateOnly? DeliveryDate) MakeRow(
        string seiban, string itemNumber, double actualWorkMinutes, string destinationCode = "D1")
        => (seiban, itemNumber, "品目A", "M1", destinationCode, new DateOnly(2026, 6, 30), "作業者A", actualWorkMinutes, 1, null);

    private static ProcessDefinition MakeDef(string destinationCode, double leadTimeMinutes) => new() {
        DestinationCode = destinationCode,
        ProcessName = destinationCode,
        WorkTimeMinutes = leadTimeMinutes,
    };

    [Theory]
    [InlineData(109, false)]
    [InlineData(110, false)] // ちょうどしきい値は超過に含めない（厳密な超過判定）
    [InlineData(111, true)]
    public void Aggregate_PercentMode_OverStandard_UsesStrictGreaterThanThreshold(double actualWorkMinutes, bool expectedOver) {
        var rows = new[] { MakeRow("S1", "I1", actualWorkMinutes) };
        var defs = new Dictionary<string, List<ProcessDefinition>> { ["I1"] = [MakeDef("D1", 100)] };

        var result = ProcessBottleneckCalculator.Aggregate(rows, defs, BottleneckThresholdMode.Percent, 110, BottleneckThresholdMode.Percent, 80);

        Assert.Equal(expectedOver ? 1 : 0, result.Single().OverStandardCount);
    }

    [Theory]
    [InlineData(81, false)]
    [InlineData(80, false)] // ちょうどしきい値は未達に含めない（厳密な未達判定）
    [InlineData(79, true)]
    public void Aggregate_PercentMode_UnderStandard_UsesStrictLessThanThreshold(double actualWorkMinutes, bool expectedUnder) {
        var rows = new[] { MakeRow("S1", "I1", actualWorkMinutes) };
        var defs = new Dictionary<string, List<ProcessDefinition>> { ["I1"] = [MakeDef("D1", 100)] };

        var result = ProcessBottleneckCalculator.Aggregate(rows, defs, BottleneckThresholdMode.Percent, 110, BottleneckThresholdMode.Percent, 80);

        Assert.Equal(expectedUnder ? 1 : 0, result.Single().UnderStandardCount);
    }

    [Fact]
    public void Aggregate_FixedMinutesMode_OverAndUnderStandard_UseAbsoluteMinuteOffsets() {
        // 標準時間100分に対し、超過しきい値=100+10=110分、未達しきい値=100-10=90分
        var rows = new[] {
            MakeRow("S1", "I1", 111), // 110分を超えるため超過
            MakeRow("S2", "I1", 89),  // 90分を下回るため未達
            MakeRow("S3", "I1", 100), // ちょうど標準時間のためどちらでもない
        };
        var defs = new Dictionary<string, List<ProcessDefinition>> { ["I1"] = [MakeDef("D1", 100)] };

        var result = ProcessBottleneckCalculator.Aggregate(rows, defs, BottleneckThresholdMode.FixedMinutes, 10, BottleneckThresholdMode.FixedMinutes, 10);

        var row = result.Single();
        Assert.Equal(3, row.CompletedCount);
        Assert.Equal(1, row.OverStandardCount);
        Assert.Equal(1, row.UnderStandardCount);
    }

    [Fact]
    public void Aggregate_RequiredMinutesZero_ExcludedFromOverAndUnderCounts() {
        // 標準時間が未設定（0分）の工程は、超過・未達どちらの判定からも除外する
        var rows = new[] { MakeRow("S1", "I1", 500) };
        var defs = new Dictionary<string, List<ProcessDefinition>> { ["I1"] = [MakeDef("D1", 0)] };

        var result = ProcessBottleneckCalculator.Aggregate(rows, defs, BottleneckThresholdMode.Percent, 110, BottleneckThresholdMode.Percent, 80);

        var row = result.Single();
        Assert.Equal(0, row.OverStandardCount);
        Assert.Equal(0, row.UnderStandardCount);
    }

    [Fact]
    public void Aggregate_Items_SortedByOverMinutesDescending() {
        var rows = new[] {
            MakeRow("S1", "I1", 105), // 標準100分に対し+5分
            MakeRow("S2", "I1", 130), // +30分（最大の外れ値）
            MakeRow("S3", "I1", 90),  // -10分
        };
        var defs = new Dictionary<string, List<ProcessDefinition>> { ["I1"] = [MakeDef("D1", 100)] };

        var result = ProcessBottleneckCalculator.Aggregate(rows, defs, BottleneckThresholdMode.Percent, 110, BottleneckThresholdMode.Percent, 80);

        Assert.Equal(["S2", "S1", "S3"], result.Single().Items.Select(i => i.Seiban));
    }
}
