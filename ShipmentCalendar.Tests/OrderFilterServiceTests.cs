using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Tests;

public class OrderFilterServiceTests {
    private static readonly ProductCategoryClassifier _emptyClassifier = new([], [], []);

    private static Order MakeOrder(
        string itemNumber = "I1", string productName = "製品A", string manufactureNumber = "M1",
        DateOnly? deliveryDate = null, string modelCode = "", List<OrderProcess>? processes = null)
        => new() {
            ItemNumber = itemNumber,
            ProductName = productName,
            ManufactureNumber = manufactureNumber,
            DeliveryDate = deliveryDate ?? new DateOnly(2026, 1, 10),
            ModelCode = modelCode,
            Processes = processes ?? [],
        };

    private static OrderProcess MakeProcess(
        ProcessStatus status, int sortOrder = 0, int departmentId = 0,
        DateOnly? startDate = null, DateOnly? dueDate = null)
        => new() {
            Status = status,
            SortOrder = sortOrder,
            DepartmentId = departmentId,
            StartDate = startDate ?? new DateOnly(2026, 1, 1),
            DueDate = dueDate ?? new DateOnly(2026, 1, 5),
        };

    private static List<Order> Apply(IEnumerable<Order> orders, OrderFilterCriteria criteria)
        => OrderFilterService.Apply(orders, criteria, _emptyClassifier, SortMode.DeliveryDate, showDueDateForNotStarted: false);

    [Fact]
    public void Apply_ItemNumberFilter_IsCaseInsensitivePartialMatch() {
        var orders = new[] { MakeOrder(itemNumber: "abc-123"), MakeOrder(itemNumber: "xyz-999") };

        var result = Apply(orders, new OrderFilterCriteria { ItemNumber = "ABC" });

        Assert.Equal(["abc-123"], result.Select(o => o.ItemNumber));
    }

    [Fact]
    public void Apply_DeliveryDateRange_ExcludesOrdersOutsideRange() {
        var orders = new[] {
            MakeOrder(itemNumber: "before", deliveryDate: new DateOnly(2026, 1, 1)),
            MakeOrder(itemNumber: "inside", deliveryDate: new DateOnly(2026, 1, 10)),
            MakeOrder(itemNumber: "after", deliveryDate: new DateOnly(2026, 1, 20)),
        };

        var result = Apply(orders, new OrderFilterCriteria {
            DeliveryFrom = new DateTime(2026, 1, 5),
            DeliveryTo = new DateTime(2026, 1, 15),
        });

        Assert.Equal(["inside"], result.Select(o => o.ItemNumber));
    }

    [Fact]
    public void Apply_HideCompleted_ExcludesOnlyFullyCompletedOrders_ButKeepsOrdersWithNoProcesses() {
        var orders = new[] {
            MakeOrder(itemNumber: "fully-done", processes: [MakeProcess(ProcessStatus.Completed)]),
            MakeOrder(itemNumber: "in-progress", processes: [MakeProcess(ProcessStatus.Completed), MakeProcess(ProcessStatus.InProgress, sortOrder: 1)]),
            MakeOrder(itemNumber: "no-processes", processes: []),
        };

        var result = Apply(orders, new OrderFilterCriteria { HideCompleted = true });

        Assert.Equal(["in-progress", "no-processes"], result.Select(o => o.ItemNumber));
    }

    [Fact]
    public void Apply_TodayTaskOnly_MatchesOrdersWhoseNextProcessSpansToday() {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var orders = new[] {
            MakeOrder(itemNumber: "spans-today", processes: [MakeProcess(ProcessStatus.InProgress, startDate: today.AddDays(-1), dueDate: today.AddDays(1))]),
            MakeOrder(itemNumber: "future-only", processes: [MakeProcess(ProcessStatus.NotStarted, startDate: today.AddDays(5), dueDate: today.AddDays(10))]),
        };

        var result = Apply(orders, new OrderFilterCriteria { TodayTaskOnly = true });

        Assert.Equal(["spans-today"], result.Select(o => o.ItemNumber));
    }

    [Fact]
    public void Apply_TodayTaskOnly_IncludesStartDateAndDueDateBoundaries() {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var orders = new[] {
            MakeOrder(itemNumber: "starts-today", processes: [MakeProcess(ProcessStatus.InProgress, startDate: today, dueDate: today.AddDays(3))]),
            MakeOrder(itemNumber: "due-today", processes: [MakeProcess(ProcessStatus.InProgress, startDate: today.AddDays(-3), dueDate: today)]),
            MakeOrder(itemNumber: "starts-tomorrow", processes: [MakeProcess(ProcessStatus.NotStarted, startDate: today.AddDays(1), dueDate: today.AddDays(5))]),
        };

        var result = Apply(orders, new OrderFilterCriteria { TodayTaskOnly = true });

        Assert.Equal(["starts-today", "due-today"], result.Select(o => o.ItemNumber));
    }

    [Fact]
    public void Apply_CompletedOnly_ExcludesOrdersWithNoProcesses() {
        var orders = new[] { MakeOrder(itemNumber: "no-processes", processes: []) };

        var result = Apply(orders, new OrderFilterCriteria { CompletedOnly = true });

        Assert.Empty(result);
    }

    [Fact]
    public void Apply_CompletedOnly_MatchesOrdersWhereAllProcessesAreCompleted() {
        var orders = new[] {
            MakeOrder(itemNumber: "fully-done", processes: [MakeProcess(ProcessStatus.Completed), MakeProcess(ProcessStatus.Completed, sortOrder: 1)]),
            MakeOrder(itemNumber: "partially-done", processes: [MakeProcess(ProcessStatus.Completed), MakeProcess(ProcessStatus.InProgress, sortOrder: 1)]),
        };

        var result = Apply(orders, new OrderFilterCriteria { CompletedOnly = true });

        Assert.Equal(["fully-done"], result.Select(o => o.ItemNumber));
    }

    [Fact]
    public void Apply_NotStartedOnly_MatchesOrdersWhoseNextIncompleteProcessIsNotStarted() {
        var orders = new[] {
            MakeOrder(itemNumber: "not-started-next", processes: [MakeProcess(ProcessStatus.Completed), MakeProcess(ProcessStatus.NotStarted, sortOrder: 1)]),
            MakeOrder(itemNumber: "in-progress-next", processes: [MakeProcess(ProcessStatus.InProgress)]),
            MakeOrder(itemNumber: "in-progress-before-not-started", processes: [
                MakeProcess(ProcessStatus.InProgress, sortOrder: 0),
                MakeProcess(ProcessStatus.NotStarted, sortOrder: 1),
            ]),
        };

        var result = Apply(orders, new OrderFilterCriteria { NotStartedOnly = true });

        Assert.Equal(["not-started-next"], result.Select(o => o.ItemNumber));
    }

    [Fact]
    public void Apply_MultipleStatusFiltersCombinedWithHideCompleted_MatchesUnionMinusFullyCompleted() {
        var orders = new[] {
            MakeOrder(itemNumber: "overdue", processes: [MakeProcess(ProcessStatus.Overdue)]),
            MakeOrder(itemNumber: "not-started", processes: [MakeProcess(ProcessStatus.NotStarted)]),
            MakeOrder(itemNumber: "in-progress", processes: [MakeProcess(ProcessStatus.InProgress)]),
            MakeOrder(itemNumber: "fully-done", processes: [MakeProcess(ProcessStatus.Completed)]),
        };

        var result = Apply(orders, new OrderFilterCriteria { HideCompleted = true, OverdueOnly = true, NotStartedOnly = true });

        Assert.Equal(["overdue", "not-started"], result.Select(o => o.ItemNumber));
    }

    [Fact]
    public void Apply_ProductCategoryProduct_OnlyIncludesOrdersWithRegisteredProductModelCode() {
        var classifier = new ProductCategoryClassifier(productModelCodes: ["P1"], semiProductModelCodes: ["S1"], registeredItemNumbers: []);
        var orders = new[] {
            MakeOrder(itemNumber: "product", modelCode: "P1"),
            MakeOrder(itemNumber: "semi", modelCode: "S1"),
        };

        var result = OrderFilterService.Apply(orders, new OrderFilterCriteria { ProductCategory = "製品" }, classifier, SortMode.DeliveryDate, false);

        Assert.Equal(["product"], result.Select(o => o.ItemNumber));
    }

    [Fact]
    public void Apply_DepartmentIdFilter_MatchesByNextIncompleteProcessDepartment() {
        var orders = new[] {
            MakeOrder(itemNumber: "dept1-next", processes: [
                MakeProcess(ProcessStatus.Completed, sortOrder: 0, departmentId: 2),
                MakeProcess(ProcessStatus.NotStarted, sortOrder: 1, departmentId: 1),
            ]),
            MakeOrder(itemNumber: "dept2-next", processes: [
                MakeProcess(ProcessStatus.NotStarted, sortOrder: 0, departmentId: 2),
            ]),
        };

        var result = Apply(orders, new OrderFilterCriteria { DepartmentId = 1 });

        Assert.Equal(["dept1-next"], result.Select(o => o.ItemNumber));
    }

    [Fact]
    public void Apply_SortModeDeliveryDate_OrdersAscendingByDeliveryDate() {
        var orders = new[] {
            MakeOrder(itemNumber: "later", deliveryDate: new DateOnly(2026, 2, 1)),
            MakeOrder(itemNumber: "earlier", deliveryDate: new DateOnly(2026, 1, 1)),
        };

        var result = OrderFilterService.Apply(orders, new OrderFilterCriteria(), _emptyClassifier, SortMode.DeliveryDate, false);

        Assert.Equal(["earlier", "later"], result.Select(o => o.ItemNumber));
    }

    [Fact]
    public void GetNextProcessSortDate_AllProcessesCompleted_ReturnsMaxValue() {
        var order = MakeOrder(processes: [MakeProcess(ProcessStatus.Completed)]);

        var result = OrderFilterService.GetNextProcessSortDate(order, showDueDateForNotStarted: true);

        Assert.Equal(DateOnly.MaxValue, result);
    }

    [Fact]
    public void GetNextProcessSortDate_ShowDueDateForNotStartedToggle_SwitchesBetweenDueDateAndStartDate() {
        var order = MakeOrder(processes: [
            MakeProcess(ProcessStatus.NotStarted, startDate: new DateOnly(2026, 1, 1), dueDate: new DateOnly(2026, 1, 5)),
        ]);

        Assert.Equal(new DateOnly(2026, 1, 5), OrderFilterService.GetNextProcessSortDate(order, showDueDateForNotStarted: true));
        Assert.Equal(new DateOnly(2026, 1, 1), OrderFilterService.GetNextProcessSortDate(order, showDueDateForNotStarted: false));
    }
}
