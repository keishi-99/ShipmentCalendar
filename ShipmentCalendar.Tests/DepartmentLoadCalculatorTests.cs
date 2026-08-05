using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Tests;

public class DepartmentLoadCalculatorTests
{
    private static readonly Department[] _noDepartments = [];

    // 特記のない限り、1人=480分/日・充足率80%以上でCaution・100%以上でConcentratedとして扱う
    private const double DayMinutes = 480;
    private const double CautionPercent = 80;
    private const double ConcentratedPercent = 100;

    // 特記のない限り、工程は1営業日で完結する（DailyMinutes = { [dueDate] = requiredMinutes }）ものとして扱う
    private static OrderProcess MakeProcess(int departmentId, DateOnly dueDate, double requiredMinutes, ProcessStatus status = ProcessStatus.NotStarted, string destinationCode = "P1") => new() {
        DepartmentId = departmentId,
        StartDate = dueDate,
        DueDate = dueDate,
        RequiredMinutes = requiredMinutes,
        DailyMinutes = requiredMinutes > 0 ? new() { [dueDate] = requiredMinutes } : [],
        Status = status,
        DestinationCode = destinationCode,
        ProcessName = destinationCode
    };

    // 複数日にまたがる工程用に、日別内訳を直接指定できるヘルパー
    private static OrderProcess MakeMultiDayProcess(int departmentId, DateOnly startDate, DateOnly dueDate, Dictionary<DateOnly, double> dailyMinutes, ProcessStatus status = ProcessStatus.NotStarted, string destinationCode = "P1") => new() {
        DepartmentId = departmentId,
        StartDate = startDate,
        DueDate = dueDate,
        RequiredMinutes = dailyMinutes.Values.Sum(),
        DailyMinutes = dailyMinutes,
        Status = status,
        DestinationCode = destinationCode,
        ProcessName = destinationCode
    };

    private static Order MakeOrder(string manufactureNumber, params OrderProcess[] processes) => new() {
        ManufactureNumber = manufactureNumber,
        Processes = [.. processes]
    };

    // 他のテストが使う日付（2026/6/26〜7/3）より確実に前にし、todayを指定しないテストが超過扱いにならないようにする
    private static readonly DateOnly _defaultToday = new(2026, 6, 1);

    private static List<DepartmentLoadRow> Aggregate(IEnumerable<Order> orders, IEnumerable<Department> departments,
        IEnumerable<DepartmentAbsence>? absences = null,
        double dayMinutes = DayMinutes, double cautionPercent = CautionPercent, double concentratedPercent = ConcentratedPercent,
        DateOnly? today = null)
        => DepartmentLoadCalculator.Aggregate(orders, departments, absences ?? [], dayMinutes, cautionPercent, concentratedPercent, today ?? _defaultToday);

    [Fact]
    public void Aggregate_NoOrders_ReturnsEmptyList() {
        var rows = Aggregate([], _noDepartments);

        Assert.Empty(rows);
    }

    [Fact]
    public void Aggregate_AllProcessesCompleted_ReturnsEmptyList() {
        var order = MakeOrder("M1", MakeProcess(1, new DateOnly(2026, 6, 30), 100, ProcessStatus.Completed));

        var rows = Aggregate([order], _noDepartments);

        Assert.Empty(rows);
    }

    [Fact]
    public void Aggregate_SameDepartmentAndDate_SumsCountAndMinutes() {
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1",
            MakeProcess(1, dueDate, 100, destinationCode: "P1"),
            MakeProcess(1, dueDate, 150, destinationCode: "P2"));
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order], departments);

        var cell = rows.Single(r => r.DepartmentName == "総務部").Cells.Single(c => c.Date == dueDate);
        Assert.Equal(2, cell.ProcessCount);
        Assert.Equal(250, cell.TotalMinutes);
    }

    [Fact]
    public void Aggregate_UnknownDepartmentId_AddsFallbackRow() {
        var order = MakeOrder("M1", MakeProcess(departmentId: 0, new DateOnly(2026, 6, 30), 100));

        var rows = Aggregate([order], _noDepartments);

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

        var rows = Aggregate([order], departments);

        var manufacturingRow = rows.Single(r => r.DepartmentName == "製造部");
        Assert.All(manufacturingRow.Cells, c => Assert.Equal(0, c.ProcessCount));
    }

    [Fact]
    public void Aggregate_DateRange_FillsEveryDateBetweenMinAndMaxDueDate() {
        var order = MakeOrder("M1",
            MakeProcess(1, new DateOnly(2026, 6, 30), 100, destinationCode: "P1"),
            MakeProcess(1, new DateOnly(2026, 7, 3), 100, destinationCode: "P2"));
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order], departments);

        var dates = rows.Single().Cells.Select(c => c.Date).ToList();
        // 6/30〜7/3の4日分（休日も含めて連続で埋める。営業日判定はしない）
        Assert.Equal([new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 3)], dates);
    }

    [Fact]
    public void Aggregate_Cell_CarriesOrderAndProcessForDrilldown() {
        var dueDate = new DateOnly(2026, 6, 30);
        var process = MakeProcess(1, dueDate, 100);
        var order = MakeOrder("M1", process);
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order], departments);

        var item = rows.Single().Cells.Single(c => c.Date == dueDate).Items.Single();
        Assert.Same(order, item.Order);
        Assert.Same(process, item.Process);
    }

    [Fact]
    public void Aggregate_ProcessSpansMultipleDays_SplitsMinutesPerDay() {
        var startDate = new DateOnly(2026, 6, 29);
        var dueDate = new DateOnly(2026, 6, 30);
        var process = MakeMultiDayProcess(1, startDate, dueDate, new() { [startDate] = 180, [dueDate] = 20 });
        var order = MakeOrder("M1", process);
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order], departments);

        var cells = rows.Single().Cells;
        Assert.Equal(180, cells.Single(c => c.Date == startDate).TotalMinutes);
        Assert.Equal(20, cells.Single(c => c.Date == dueDate).TotalMinutes);
    }

    [Fact]
    public void Aggregate_ProcessSpansMultipleDays_DrilldownItemShowsThatDaysMinutes() {
        var startDate = new DateOnly(2026, 6, 29);
        var dueDate = new DateOnly(2026, 6, 30);
        var process = MakeMultiDayProcess(1, startDate, dueDate, new() { [startDate] = 180, [dueDate] = 20 });
        var order = MakeOrder("M1", process);
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order], departments);

        var cells = rows.Single().Cells;
        Assert.Equal(180, cells.Single(c => c.Date == startDate).Items.Single().DayMinutes);
        Assert.Equal(20, cells.Single(c => c.Date == dueDate).Items.Single().DayMinutes);
    }

    [Fact]
    public void Aggregate_SingleDayProcess_TextPropertiesShowOnlyDayMinutes() {
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, 90));
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order], departments);

        var item = rows.Single().Cells.Single(c => c.Date == dueDate).Items.Single();
        Assert.Equal("1.5h", item.DayMinutesText);
        Assert.Equal(string.Empty, item.TotalMinutesText);
        Assert.Equal(string.Empty, item.ProgressText);
    }

    [Fact]
    public void Aggregate_ProcessSpansMultipleDays_ProgressTextShowsDayIndexOutOfTotal() {
        var startDate = new DateOnly(2026, 6, 29);
        var dueDate = new DateOnly(2026, 6, 30);
        var process = MakeMultiDayProcess(1, startDate, dueDate, new() { [startDate] = 180, [dueDate] = 20 });
        var order = MakeOrder("M1", process);
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order], departments);

        var cells = rows.Single().Cells;
        var firstDayItem = cells.Single(c => c.Date == startDate).Items.Single();
        var lastDayItem = cells.Single(c => c.Date == dueDate).Items.Single();
        Assert.Equal("1/2日目", firstDayItem.ProgressText);
        Assert.Equal("2/2日目", lastDayItem.ProgressText);
        Assert.Equal("3.3h", firstDayItem.TotalMinutesText);
        Assert.Equal("3.3h", lastDayItem.TotalMinutesText);
    }

    [Fact]
    public void Aggregate_ProcessSpansNonContiguousBusinessDays_ProgressTextCountsByRankNotCalendarGap() {
        // 休日で日付が飛んでいても、DailyMinutesのキー数（営業日の順位）でカウントすることを確認する
        var day1 = new DateOnly(2026, 6, 26);
        var day2 = new DateOnly(2026, 6, 29); // 6/27・6/28は休日等で飛ばされている想定
        var day3 = new DateOnly(2026, 6, 30);
        var process = MakeMultiDayProcess(1, day1, day3, new() { [day1] = 100, [day2] = 100, [day3] = 100 });
        var order = MakeOrder("M1", process);
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order], departments);

        var cells = rows.Single().Cells;
        Assert.Equal("1/3日目", cells.Single(c => c.Date == day1).Items.Single().ProgressText);
        Assert.Equal("2/3日目", cells.Single(c => c.Date == day2).Items.Single().ProgressText);
        Assert.Equal("3/3日目", cells.Single(c => c.Date == day3).Items.Single().ProgressText);
    }

    [Fact]
    public void Aggregate_HeadcountNotSet_AlwaysNormalRegardlessOfMinutes() {
        // 基本人数が未設定（0）だと充足率を計算できないため、必要時間がどれだけ大きくてもNormalのまま
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, 10000));
        var departments = new[] { new Department { Id = 1, Name = "総務部", Headcount = 0 } };

        var rows = Aggregate([order], departments);

        var cell = rows.Single().Cells.Single(c => c.Date == dueDate);
        Assert.Equal(CongestionLevel.Normal, cell.Level);
        Assert.Null(cell.FulfillmentPercent);
    }

    [Theory]
    [InlineData(383, CongestionLevel.Normal)]
    [InlineData(384, CongestionLevel.Caution)]
    [InlineData(479, CongestionLevel.Caution)]
    [InlineData(480, CongestionLevel.Concentrated)]
    public void Aggregate_ThresholdBoundaries_ClassifyCongestionLevelByFulfillmentPercent(double requiredMinutes, CongestionLevel expected) {
        // Headcount=1・DayMinutes=480の部署のキャパシティは480分。
        // 384分=80%（Caution境界）、480分=100%（Concentrated境界）
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, requiredMinutes));
        var departments = new[] { new Department { Id = 1, Name = "総務部", Headcount = 1 } };

        var rows = Aggregate([order], departments);

        var cell = rows.Single().Cells.Single(c => c.Date == dueDate);
        Assert.Equal(expected, cell.Level);
    }

    [Fact]
    public void Aggregate_Cell_CalculatesFulfillmentPercentFromHeadcountAndDayMinutes() {
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, 240));
        var departments = new[] { new Department { Id = 1, Name = "総務部", Headcount = 1 } };

        var rows = Aggregate([order], departments);

        var cell = rows.Single().Cells.Single(c => c.Date == dueDate);
        // 240分 ÷ (1人 × 480分) × 100 = 50%
        Assert.Equal(50, cell.FulfillmentPercent);
    }

    [Fact]
    public void Aggregate_AbsenceReducesActiveHeadcount_RaisesFulfillmentPercent() {
        // 基本人数2人・欠員1人 → 実働人数1人（キャパシティ480分）。240分÷480分=50%
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, 240));
        var departments = new[] { new Department { Id = 1, Name = "総務部", Headcount = 2 } };
        var absences = new[] { new DepartmentAbsence { DepartmentId = 1, Date = dueDate, AbsentCount = 1 } };

        var rows = Aggregate([order], departments, absences);

        var cell = rows.Single().Cells.Single(c => c.Date == dueDate);
        Assert.Equal(50, cell.FulfillmentPercent);
    }

    [Fact]
    public void Aggregate_Cell_DisplayTextIncludesAbsentCountWhenPresent() {
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, 240));
        var departments = new[] { new Department { Id = 1, Name = "総務部", Headcount = 2 } };
        var absences = new[] { new DepartmentAbsence { DepartmentId = 1, Date = dueDate, AbsentCount = 1 } };

        var rows = Aggregate([order], departments, absences);

        var cell = rows.Single().Cells.Single(c => c.Date == dueDate);
        Assert.Equal("50%", cell.FulfillmentPercentText);
        Assert.Equal("欠1", cell.AbsentCellText);
    }

    [Fact]
    public void Aggregate_Cell_DisplayTextIncludesOvertimeHoursWhenOverCapacity() {
        // 実働人数1人・稼働480分に対し必要時間600分 → 充足率125%、超過120分(2.0h)
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, 600));
        var departments = new[] { new Department { Id = 1, Name = "総務部", Headcount = 1 } };

        var rows = Aggregate([order], departments);

        var cell = rows.Single().Cells.Single(c => c.Date == dueDate);
        Assert.Equal(120, cell.ExcessMinutes);
        Assert.Equal("125%", cell.FulfillmentPercentText);
        Assert.Equal("(+2.0h)", cell.OvertimeText);
    }

    [Fact]
    public void Aggregate_Cell_DisplayTextOmitsOvertimeHoursWhenUnderCapacity() {
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, 240));
        var departments = new[] { new Department { Id = 1, Name = "総務部", Headcount = 1 } };

        var rows = Aggregate([order], departments);

        var cell = rows.Single().Cells.Single(c => c.Date == dueDate);
        Assert.Null(cell.ExcessMinutes);
        Assert.Equal(string.Empty, cell.OvertimeText);
    }

    [Fact]
    public void Aggregate_Cell_FulfillmentCellTextEmptyWhenNoProcesses() {
        // 工程が無い日でも基本人数が設定されていれば充足率0%が算出されるが、対象工程が無いためセル表示では省略する
        var date1 = new DateOnly(2026, 6, 30);
        var emptyDate = new DateOnly(2026, 7, 1);
        var date2 = new DateOnly(2026, 7, 2);
        var order = MakeOrder("M1", MakeProcess(1, date1, 240), MakeProcess(1, date2, 100, destinationCode: "P2"));
        var departments = new[] { new Department { Id = 1, Name = "総務部", Headcount = 2 } };

        var rows = Aggregate([order], departments);

        var emptyCell = rows.Single().Cells.Single(c => c.Date == emptyDate);
        Assert.Equal(0, emptyCell.ProcessCount);
        Assert.Equal(0, emptyCell.FulfillmentPercent);
        Assert.Equal(string.Empty, emptyCell.FulfillmentPercentText);
    }

    [Fact]
    public void Aggregate_AbsenceExceedsHeadcount_ClampsActiveHeadcountToZero() {
        // 基本人数1人・欠員2人 → 実働人数は0未満にならずクランプされ、充足率は算出不能（Normal固定）
        var dueDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, 100));
        var departments = new[] { new Department { Id = 1, Name = "総務部", Headcount = 1 } };
        var absences = new[] { new DepartmentAbsence { DepartmentId = 1, Date = dueDate, AbsentCount = 2 } };

        var rows = Aggregate([order], departments, absences);

        var cell = rows.Single().Cells.Single(c => c.Date == dueDate);
        Assert.Null(cell.FulfillmentPercent);
        Assert.Equal(CongestionLevel.Normal, cell.Level);
    }

    [Fact]
    public void Aggregate_Cell_ItemsAreSortedByDayMinutesDescending() {
        // 同日・同部署に複数注文が集計される場合、必要時間が大きい注文ほど先頭に来て、
        // その日の集中度への主要因をすぐ確認できるようにする
        var dueDate = new DateOnly(2026, 6, 30);
        var order1 = MakeOrder("M1", MakeProcess(1, dueDate, 50, destinationCode: "P1"));
        var order2 = MakeOrder("M2", MakeProcess(1, dueDate, 200, destinationCode: "P2"));
        var order3 = MakeOrder("M3", MakeProcess(1, dueDate, 100, destinationCode: "P3"));
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order1, order2, order3], departments);

        var items = rows.Single().Cells.Single(c => c.Date == dueDate).Items;
        Assert.Equal([200, 100, 50], items.Select(i => i.DayMinutes));
    }

    /// <summary>
    /// しきい値を0にすると、fulfillmentPercent(0) &gt;= cautionPercent(0)でCautionと誤判定されうる境界値。
    /// CodeRabbitの指摘で見つかった不具合（分数閾値版）の再発防止を、充足率版でも維持する。
    /// </summary>
    [Fact]
    public void Aggregate_CautionThresholdIsZero_EmptyCellStaysNormal() {
        // 部署2に7/2の工程を混ぜて日付範囲を6/30〜7/2に広げ、部署1の7/1を「データなしの空セル」にする
        var order1 = MakeOrder("M1", MakeProcess(1, new DateOnly(2026, 6, 30), 100));
        var order2 = MakeOrder("M2", MakeProcess(2, new DateOnly(2026, 7, 2), 100));
        var departments = new[] {
            new Department { Id = 1, Name = "総務部", Headcount = 1 },
            new Department { Id = 2, Name = "製造部", Headcount = 1 },
        };

        var rows = Aggregate([order1, order2], departments, cautionPercent: 0);

        var emptyCell = rows.Single(r => r.DepartmentName == "総務部").Cells.Single(c => c.Date == new DateOnly(2026, 7, 1));
        Assert.Equal(0, emptyCell.ProcessCount);
        Assert.Equal(CongestionLevel.Normal, emptyCell.Level);
    }

    [Fact]
    public void Aggregate_ProcessDueDateBeforeToday_GoesToOverdueNotCells() {
        var dueDate = new DateOnly(2026, 6, 30);
        var today = new DateOnly(2026, 7, 5);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, 100));
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order], departments, today: today);

        var row = rows.Single();
        Assert.Empty(row.Cells);
        Assert.Equal(1, row.OverdueProcessCount);
        Assert.Equal(100, row.OverdueMinutes);
        Assert.Same(order, row.OverdueItems.Single().Order);
    }

    [Fact]
    public void Aggregate_OverdueMultiDayProcess_KeepsPerDayBreakdownInOverdueItems() {
        // 超過と判定された工程でも、日ごとの内訳（進捗表示等）は失わず「超過」列の明細に残す
        var day1 = new DateOnly(2026, 6, 29);
        var day2 = new DateOnly(2026, 6, 30);
        var today = new DateOnly(2026, 7, 5);
        var process = MakeMultiDayProcess(1, day1, day2, new() { [day1] = 180, [day2] = 20 });
        var order = MakeOrder("M1", process);
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order], departments, today: today);

        var items = rows.Single().OverdueItems;
        Assert.Equal(2, items.Count);
        Assert.Equal(180, items.Single(i => i.Date == day1).DayMinutes);
        Assert.Equal("1/2日目", items.Single(i => i.Date == day1).ProgressText);
        Assert.Equal(20, items.Single(i => i.Date == day2).DayMinutes);
        Assert.Equal("2/2日目", items.Single(i => i.Date == day2).ProgressText);
    }

    [Fact]
    public void Aggregate_MultiDayProcessDueDateNotYetPassed_EarlyPastDaysStayInCellsNotOverdue() {
        // ユーザー報告の再現1: 完了判定は工程単位（最終日確定時）でしか行われず日ごとには追跡されないため、
        // DueDateがまだ来ていない複数日工程は、先頭側の日の日付が経過しているだけでは超過にしてはならない
        var day1 = new DateOnly(2026, 6, 29);
        var day2 = new DateOnly(2026, 6, 30); // DueDate。今日はこれと同日＝まだ超過ではない
        var process = MakeMultiDayProcess(1, day1, day2, new() { [day1] = 180, [day2] = 20 });
        var order = MakeOrder("M1", process);
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order], departments, today: day2);

        var row = rows.Single();
        Assert.Equal(0, row.OverdueProcessCount);
        Assert.Empty(row.OverdueItems);
        Assert.Equal(180, row.Cells.Single(c => c.Date == day1).TotalMinutes);
        Assert.Equal(20, row.Cells.Single(c => c.Date == day2).TotalMinutes);
    }

    [Fact]
    public void Aggregate_StatusOverdueByPropagationButOwnDueDateNotYetPassed_StaysInCellsNotOverdue() {
        // ユーザー報告の再現2: OrderProcessBuildService.Buildは前工程が超過すると後続工程のStatusも
        // Overdueに伝播させる（メイン画面の色分け用）。この工程自身のDueDateはまだ先なのに
        // Statusだけ伝播でOverdueになっているケースで、誤って超過集計に含めてはならない
        var dueDate = new DateOnly(2026, 7, 10);
        var today = new DateOnly(2026, 7, 5);
        var order = MakeOrder("M1", MakeProcess(1, dueDate, 100, status: ProcessStatus.Overdue));
        var departments = new[] { new Department { Id = 1, Name = "総務部" } };

        var rows = Aggregate([order], departments, today: today);

        var row = rows.Single();
        Assert.Equal(0, row.OverdueProcessCount);
        Assert.Empty(row.OverdueItems);
        Assert.Equal(100, row.Cells.Single(c => c.Date == dueDate).TotalMinutes);
    }

    [Fact]
    public void Aggregate_OverdueOnly_UnknownDepartment_AddsFallbackRow() {
        var dueDate = new DateOnly(2026, 6, 30);
        var today = new DateOnly(2026, 7, 5);
        var order = MakeOrder("M1", MakeProcess(departmentId: 0, dueDate, 100));

        var rows = Aggregate([order], _noDepartments, today: today);

        Assert.Contains(rows, r => r.DepartmentName == "未設定");
    }
}
