using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Tests;

/// <summary>最終受入の救済ルール（MarkAllCompletedIfFinalReceiptDone）と、実績工程の組み立て（BuildActualProcesses）のテスト</summary>
public class ActualProcessTests {
    private static ProcessDefinition Def(string destCode, int sortOrder, string processName = "工程", int departmentId = 0, bool isVisible = true)
        => new() {
            DestinationCode = destCode,
            SortOrder = sortOrder,
            ProcessName = processName,
            DepartmentId = departmentId,
            IsVisible = isVisible,
        };

    private static (string Seiban, string DestinationCode, DateOnly ActualDate, string WorkerName, double ActualWorkMinutes) Row(
        string seiban, string destCode, DateOnly actualDate, string worker = "作業者", double minutes = 10)
        => (seiban, destCode, actualDate, worker, minutes);

    private static Dictionary<string, (DateOnly? ActualDate, string WorkerName, double ActualWorkMinutes)> Completed(params string[] destCodes)
        => destCodes.ToDictionary(c => c, _ => ((DateOnly?)new DateOnly(2026, 6, 1), "作業者", 0.0));

    private static List<OrderProcess> MakeProcesses(params ProcessStatus[] statuses)
        => [.. statuses.Select(s => new OrderProcess { Status = s })];

    // ---- MarkAllCompletedIfFinalReceiptDone ----

    [Fact]
    public void MarkAllCompleted_FinalReceiptDone_MarksEveryProcessCompleted() {
        var processes = MakeProcesses(ProcessStatus.NotStarted, ProcessStatus.Overdue, ProcessStatus.InProgress);
        var defs = new[] { Def("D1", 1), Def("D999", ProcessDefinition.FinalReceiptSortOrder) };

        BusinessDayCalculator.MarkAllCompletedIfFinalReceiptDone(processes, defs, Completed("D999"));

        Assert.All(processes, p => Assert.Equal(ProcessStatus.Completed, p.Status));
    }

    [Fact]
    public void MarkAllCompleted_FinalReceiptNotDone_LeavesStatusUnchanged() {
        var processes = MakeProcesses(ProcessStatus.NotStarted, ProcessStatus.Overdue);
        var defs = new[] { Def("D1", 1), Def("D999", ProcessDefinition.FinalReceiptSortOrder) };

        BusinessDayCalculator.MarkAllCompletedIfFinalReceiptDone(processes, defs, Completed("D1"));

        Assert.Equal([ProcessStatus.NotStarted, ProcessStatus.Overdue], processes.Select(p => p.Status));
    }

    [Fact]
    public void MarkAllCompleted_NoFinalReceiptDefinition_LeavesStatusUnchanged() {
        var processes = MakeProcesses(ProcessStatus.NotStarted);
        var defs = new[] { Def("D1", 1) };

        BusinessDayCalculator.MarkAllCompletedIfFinalReceiptDone(processes, defs, Completed("D1"));

        Assert.Equal(ProcessStatus.NotStarted, Assert.Single(processes).Status);
    }

    [Fact]
    public void MarkAllCompleted_HiddenFinalReceiptDefinition_IsStillDetected() {
        var processes = MakeProcesses(ProcessStatus.NotStarted);
        var defs = new[] { Def("D999", ProcessDefinition.FinalReceiptSortOrder, isVisible: false) };

        BusinessDayCalculator.MarkAllCompletedIfFinalReceiptDone(processes, defs, Completed("D999"));

        Assert.Equal(ProcessStatus.Completed, Assert.Single(processes).Status);
    }

    // ---- BuildActualProcesses ----

    [Fact]
    public void BuildActualProcesses_SameDestinationMultipleRows_SumsMinutesAndAdoptsLatestRow() {
        var defs = new[] { Def("D1", 1) };
        var rows = new[] {
            Row("S1", "D1", new DateOnly(2026, 6, 10), worker: "古い担当", minutes: 30),
            Row("S1", "D1", new DateOnly(2026, 6, 12), worker: "新しい担当", minutes: 45),
        };

        var process = Assert.Single(Assert.Single(BusinessDayCalculator.BuildActualProcesses(defs, rows)).Value);

        Assert.Equal(75, process.ActualWorkMinutes);
        Assert.Equal(75, process.RequiredMinutes);
        Assert.Equal(new DateOnly(2026, 6, 12), process.ActualDate);
        Assert.Equal("新しい担当", process.WorkerName);
        Assert.Equal(ProcessStatus.Completed, process.Status);
    }

    [Fact]
    public void BuildActualProcesses_DuplicateDestinationCodeInDefinitions_DoesNotThrowAndFirstWins() {
        var defs = new[] { Def("D1", 1, processName: "先勝ち"), Def("d1", 2, processName: "後負け") };
        var rows = new[] { Row("S1", "D1", new DateOnly(2026, 6, 10)) };

        var process = Assert.Single(Assert.Single(BusinessDayCalculator.BuildActualProcesses(defs, rows)).Value);

        Assert.Equal("先勝ち", process.ProcessName);
    }

    [Fact]
    public void BuildActualProcesses_UnmatchedDestinationCode_IsExcluded() {
        var defs = new[] { Def("D1", 1) };
        var rows = new[] {
            Row("S1", "D1", new DateOnly(2026, 6, 10)),
            Row("S1", "UNKNOWN", new DateOnly(2026, 6, 11)),
        };

        var process = Assert.Single(Assert.Single(BusinessDayCalculator.BuildActualProcesses(defs, rows)).Value);

        Assert.Equal("D1", process.DestinationCode);
    }

    [Fact]
    public void BuildActualProcesses_SeibanWithNoMatchingDefinition_IsNotInResult() {
        var defs = new[] { Def("D1", 1) };
        var rows = new[] { Row("S1", "UNKNOWN", new DateOnly(2026, 6, 10)) };

        Assert.Empty(BusinessDayCalculator.BuildActualProcesses(defs, rows));
    }

    [Fact]
    public void BuildActualProcesses_SeibanIsGroupedCaseInsensitively() {
        var defs = new[] { Def("D1", 1), Def("D2", 2) };
        var rows = new[] {
            Row("abc", "D1", new DateOnly(2026, 6, 10)),
            Row("ABC", "D2", new DateOnly(2026, 6, 11)),
        };

        var result = BusinessDayCalculator.BuildActualProcesses(defs, rows);

        Assert.Equal(2, Assert.Single(result).Value.Count);
    }

    [Fact]
    public void BuildActualProcesses_Processes_AreOrderedBySortOrderAndCopyDefinitionFields() {
        var defs = new[] { Def("D2", 2, "後工程", departmentId: 7), Def("D1", 1, "前工程", departmentId: 3) };
        var rows = new[] {
            Row("S1", "D2", new DateOnly(2026, 6, 12)),
            Row("S1", "D1", new DateOnly(2026, 6, 10)),
        };

        var processes = Assert.Single(BusinessDayCalculator.BuildActualProcesses(defs, rows)).Value;

        Assert.Equal(["前工程", "後工程"], processes.Select(p => p.ProcessName));
        Assert.Equal([3, 7], processes.Select(p => p.DepartmentId));
    }

    [Fact]
    public void BuildActualProcesses_StartDate_IsPreviousProcessActualDateOrOwnDueDateForFirst() {
        var defs = new[] { Def("D1", 1), Def("D2", 2) };
        var rows = new[] {
            Row("S1", "D1", new DateOnly(2026, 6, 10)),
            Row("S1", "D2", new DateOnly(2026, 6, 15)),
        };

        var processes = Assert.Single(BusinessDayCalculator.BuildActualProcesses(defs, rows)).Value;

        Assert.Equal(new DateOnly(2026, 6, 10), processes[0].StartDate); // 先頭は自身の受入日
        Assert.Equal(new DateOnly(2026, 6, 10), processes[1].StartDate); // 前工程の受入日
        Assert.Equal(new DateOnly(2026, 6, 15), processes[1].DueDate);
    }

    [Theory]
    [InlineData(20, 15)] // 前工程の受入日が未来 → 自身の受入日を開始日にする
    [InlineData(15, 15)] // 同日 → 自身の受入日
    public void BuildActualProcesses_PreviousActualDateNotBeforeDueDate_StartDateFallsBackToOwnDueDate(int previousDay, int ownDay) {
        var defs = new[] { Def("D1", 1), Def("D2", 2) };
        var rows = new[] {
            Row("S1", "D1", new DateOnly(2026, 6, previousDay)),
            Row("S1", "D2", new DateOnly(2026, 6, ownDay)),
        };

        var second = Assert.Single(BusinessDayCalculator.BuildActualProcesses(defs, rows)).Value[1];

        Assert.Equal(new DateOnly(2026, 6, ownDay), second.StartDate);
        Assert.True(second.StartDate <= second.DueDate);
    }
}
