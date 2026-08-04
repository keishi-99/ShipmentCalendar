using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Tests;

public class OrderProcessBuildServiceTests {
    private static readonly Dictionary<string, string> NoDisplayNameOverrides = new();
    private static readonly Dictionary<string, int> NoLeadDaysOverrides = new();

    private static BusinessDayCalculator MakeCalculator() => new(holidays: [], dayMinutes: 480);

    private static ProcessDefinition OdbcDef(string itemNumber, string destCode, int sortOrder, double setupMinutes = 480, string processName = "ODBC工程名", bool isVisible = true)
        => new() {
            ItemNumber = itemNumber,
            DestinationCode = destCode,
            SortOrder = sortOrder,
            SetupTimeMinutes = setupMinutes,
            ProcessName = processName,
            IsVisible = isVisible,
        };

    private static Order MakeOrder(string itemNumber, DateOnly deliveryDate, List<OrderProcess>? processes = null)
        => new() {
            ItemNumber = itemNumber,
            DeliveryDate = deliveryDate,
            PlannedQuantity = 1,
            Processes = processes ?? [],
        };

    /// <summary>CLAUDE.mdで指摘されている落とし穴の回帰テスト: completedByDestNumberのタプルに載っていないフィールドは、
    /// 再ロードのたびにBuildProcesses経由で再構築されて既定値に戻ってしまう。
    /// タプルに含まれるActualDate/WorkerName/ActualWorkMinutesの3項目が正しく引き継がれることを検証する。</summary>
    [Fact]
    public void Build_CompletedProcessFields_SurviveRebuildViaCompletedByDestNumber() {
        var deliveryDate = new DateOnly(2026, 6, 30);
        var existingCompleted = new OrderProcess {
            DestinationCode = "D1",
            Status = ProcessStatus.Completed,
            ActualDate = new DateOnly(2026, 6, 25),
            WorkerName = "作業者A",
            ActualWorkMinutes = 123,
        };
        var order = MakeOrder("I1", deliveryDate, processes: [existingCompleted]);
        var defs = new List<ProcessDefinition> { OdbcDef("I1", "D1", sortOrder: 1) };

        OrderProcessBuildService.Build([order], defs, dbDefs: [], NoDisplayNameOverrides, NoLeadDaysOverrides, defaultCompletionDateLeadDays: 0, MakeCalculator(), today: deliveryDate);

        var rebuilt = Assert.Single(order.Processes);
        Assert.Equal(ProcessStatus.Completed, rebuilt.Status);
        Assert.Equal(new DateOnly(2026, 6, 25), rebuilt.ActualDate);
        Assert.Equal("作業者A", rebuilt.WorkerName);
        Assert.Equal(123, rebuilt.ActualWorkMinutes);
    }

    [Fact]
    public void Build_DbProcessDefinitionOverride_UsesDbProcessNameButOdbcSortOrder() {
        var order = MakeOrder("I1", new DateOnly(2026, 6, 30));
        var odbcDefs = new List<ProcessDefinition> { OdbcDef("I1", "D1", sortOrder: 5, processName: "ODBC名") };
        var dbDefs = new List<ProcessDefinition> {
            new() { ItemNumber = "I1", DestinationCode = "D1", ProcessName = "カスタム名", SortOrder = 999, IsVisible = true, SetupTimeMinutes = 50 },
        };

        OrderProcessBuildService.Build([order], odbcDefs, dbDefs, NoDisplayNameOverrides, NoLeadDaysOverrides, 0, MakeCalculator(), order.DeliveryDate);

        var process = Assert.Single(order.Processes);
        Assert.Equal("カスタム名", process.ProcessName);
        Assert.Equal(5, process.SortOrder); // 順序は常にODBC優先
    }

    [Fact]
    public void Build_DisplayNameOverride_ReplacesOdbcProductName() {
        var order = MakeOrder("I1", new DateOnly(2026, 6, 30));
        order.ProductName = "ODBC品目名";
        var displayNames = new Dictionary<string, string> { ["I1"] = "登録済み品目名" };

        OrderProcessBuildService.Build([order], [], [], displayNames, NoLeadDaysOverrides, 0, MakeCalculator(), order.DeliveryDate);

        Assert.Equal("登録済み品目名", order.ProductName);
    }

    [Fact]
    public void Build_LeadDaysOverride_FallsBackToDefaultWhenItemNotOverridden() {
        var order = MakeOrder("I1", new DateOnly(2026, 6, 30));
        var calculator = MakeCalculator();

        OrderProcessBuildService.Build([order], [], [], NoDisplayNameOverrides, NoLeadDaysOverrides, defaultCompletionDateLeadDays: 3, calculator, order.DeliveryDate);

        Assert.Equal(calculator.SubtractBusinessDays(order.DeliveryDate, 3), order.CompletionDate);
    }

    [Fact]
    public void Build_HiddenDefinition_IsExcludedFromResultingProcesses() {
        var order = MakeOrder("I1", new DateOnly(2026, 6, 30));
        var defs = new List<ProcessDefinition> {
            OdbcDef("I1", "D1", sortOrder: 1, isVisible: false),
            OdbcDef("I1", "D2", sortOrder: 2, isVisible: true),
        };

        OrderProcessBuildService.Build([order], defs, [], NoDisplayNameOverrides, NoLeadDaysOverrides, 0, MakeCalculator(), order.DeliveryDate);

        var process = Assert.Single(order.Processes);
        Assert.Equal("D2", process.DestinationCode);
    }

    /// <summary>先行工程（SortOrder=1）が超過すると、それ自体は単独では超過にならない後続工程（SortOrder=2）にも
    /// Overdueが伝播することを検証する。SortOrder=2は完了日直前の1日しか占有しないため、
    /// todayを2工程の期限日の間に設定することで「単独ならInProgress、伝播でOverdue」の状況を作る。</summary>
    [Fact]
    public void Build_OverdueStatus_PropagatesToSubsequentIncompleteProcess() {
        var deliveryDate = new DateOnly(2026, 6, 30);
        var order = MakeOrder("I1", deliveryDate);
        var defs = new List<ProcessDefinition> {
            OdbcDef("I1", "D1", sortOrder: 1, setupMinutes: 480), // 先行工程：期限日は完了日の1営業日前
            OdbcDef("I1", "D2", sortOrder: 2, setupMinutes: 480), // 後続工程：期限日は完了日当日
        };
        var today = deliveryDate; // 先行工程の期限（前日）は過ぎているが、後続工程の期限（当日）はまだ過ぎていない

        OrderProcessBuildService.Build([order], defs, [], NoDisplayNameOverrides, NoLeadDaysOverrides, 0, MakeCalculator(), today);

        Assert.Equal(ProcessStatus.Overdue, order.Processes.Single(p => p.SortOrder == 1).Status);
        Assert.Equal(ProcessStatus.Overdue, order.Processes.Single(p => p.SortOrder == 2).Status);
    }
}
