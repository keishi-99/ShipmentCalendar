using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Tests;

public class DetermineStatusTests {
    private static readonly DateOnly _today = new(2026, 6, 15);

    private static OrderProcess MakeProcess(DateOnly startDate, DateOnly dueDate, ProcessStatus status = ProcessStatus.NotStarted)
        => new() { StartDate = startDate, DueDate = dueDate, Status = status };

    [Fact]
    public void DetermineStatus_AlreadyCompleted_StaysCompletedEvenIfPastDueDate() {
        var process = MakeProcess(_today.AddDays(-10), _today.AddDays(-5), ProcessStatus.Completed);

        Assert.Equal(ProcessStatus.Completed, BusinessDayCalculator.DetermineStatus(process, _today, warningDays: 3));
    }

    [Fact]
    public void DetermineStatus_TodayIsAfterDueDate_ReturnsOverdue() {
        var process = MakeProcess(_today.AddDays(-3), _today.AddDays(-1));

        Assert.Equal(ProcessStatus.Overdue, BusinessDayCalculator.DetermineStatus(process, _today));
    }

    [Fact]
    public void DetermineStatus_TodayIsDueDate_IsNotOverdue() {
        var process = MakeProcess(_today.AddDays(-3), _today);

        Assert.Equal(ProcessStatus.InProgress, BusinessDayCalculator.DetermineStatus(process, _today));
    }

    [Fact]
    public void DetermineStatus_WarningDaysZero_NeverReturnsWarning() {
        var process = MakeProcess(_today.AddDays(-1), _today.AddDays(1));

        Assert.Equal(ProcessStatus.InProgress, BusinessDayCalculator.DetermineStatus(process, _today, warningDays: 0));
    }

    [Theory]
    [InlineData(0, ProcessStatus.Warning)]    // 期限当日
    [InlineData(2, ProcessStatus.Warning)]    // 警告日数ちょうど
    [InlineData(3, ProcessStatus.InProgress)] // 警告日数の1日外
    public void DetermineStatus_WarningDaysBoundary_WarnsWhenRemainingDaysAreWithinWarningDays(int daysUntilDue, ProcessStatus expected) {
        var process = MakeProcess(_today.AddDays(-1), _today.AddDays(daysUntilDue));

        Assert.Equal(expected, BusinessDayCalculator.DetermineStatus(process, _today, warningDays: 2));
    }

    [Fact]
    public void DetermineStatus_NotYetStartedButWithinWarningDays_ReturnsWarningNotNotStarted() {
        var process = MakeProcess(_today.AddDays(2), _today.AddDays(2));

        Assert.Equal(ProcessStatus.Warning, BusinessDayCalculator.DetermineStatus(process, _today, warningDays: 3));
    }

    [Fact]
    public void DetermineStatus_TodayIsStartDate_ReturnsInProgress() {
        var process = MakeProcess(_today, _today.AddDays(5));

        Assert.Equal(ProcessStatus.InProgress, BusinessDayCalculator.DetermineStatus(process, _today));
    }

    [Fact]
    public void DetermineStatus_TodayIsBeforeStartDate_ReturnsNotStarted() {
        var process = MakeProcess(_today.AddDays(1), _today.AddDays(5));

        Assert.Equal(ProcessStatus.NotStarted, BusinessDayCalculator.DetermineStatus(process, _today));
    }
}
