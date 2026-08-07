using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShipmentCalendar.Views;

public partial class DashboardWindow : Window {
    private readonly List<Order> _allOrders;
    private readonly AppSettings _settings;
    private readonly IReadOnlyList<Holiday> _holidays;
    private readonly IReadOnlyList<ProcessDefinition> _odbcProcessDefinitions;
    private readonly Func<AppSettings, IOdbcOrderRepository> _odbcOrderRepositoryFactory;
    // _allOrders自体がこの範囲（基本設定の過去日数・表示範囲日数）でしか取得されていないため、
    // 指定期間がこの範囲に収まっていればキャッシュから絞り込み、はみ出す場合のみODBCへ再取得する
    private readonly DateOnly _cachedRangeMin;
    private readonly DateOnly _cachedRangeMax;

    public DashboardWindow(IEnumerable<Order> orders, AppSettings settings, IReadOnlyList<Holiday> holidays,
        IReadOnlyList<ProcessDefinition> odbcProcessDefinitions, Func<AppSettings, IOdbcOrderRepository> odbcOrderRepositoryFactory) {
        InitializeComponent();
        _allOrders = orders.ToList();
        _settings = settings;
        _holidays = holidays;
        _odbcProcessDefinitions = odbcProcessDefinitions;
        _odbcOrderRepositoryFactory = odbcOrderRepositoryFactory;

        var today = DateOnly.FromDateTime(DateTime.Today);
        _cachedRangeMin = today.AddDays(-settings.DeliveryDatePastDays);
        _cachedRangeMax = today.AddDays(settings.DeliveryDateRangeDays);
        TxtRangeHint.Text = $"現在表示中のデータの範囲: {_cachedRangeMin:yyyy/MM/dd}～{_cachedRangeMax:yyyy/MM/dd}（この範囲外を指定するとODBCへ再取得します）";

        // 開いた直後は現在表示中のデータの範囲をそのままDatePickerに示す
        ResetToDefaultRange();

        Loaded += async (_, _) => await RefreshAsync();
    }

    private void ResetToDefaultRange() {
        StartDatePicker.SelectedDate = _cachedRangeMin.ToDateTime(TimeOnly.MinValue);
        EndDatePicker.SelectedDate = _cachedRangeMax.ToDateTime(TimeOnly.MinValue);
    }

    private async Task RefreshAsync() {
        var from = StartDatePicker.SelectedDate is { } start ? DateOnly.FromDateTime(start) : (DateOnly?)null;
        var to = EndDatePicker.SelectedDate is { } end ? DateOnly.FromDateTime(end) : (DateOnly?)null;

        SetLoading(true);
        try {
            var orders = await ResolveOrdersAsync(from, to);
            var departments = await SqliteDepartmentRepository.GetAllAsync();
            ApplySummary(DashboardSummaryCalculator.Aggregate(orders, departments));
        } catch (Exception ex) {
            MessageBox.Show($"データの取得に失敗しました: {ex.Message}", "取得エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            SetLoading(false);
        }
    }

    /// <summary>指定期間が現在キャッシュ済みの受注データの範囲に収まっていればそのまま絞り込み、
    /// はみ出す場合はODBCへその期間で直接問い合わせて取得し、工程スケジュールを組み立て直す</summary>
    private async Task<List<Order>> ResolveOrdersAsync(DateOnly? from, DateOnly? to) {
        var isWithinCachedRange = (from is null || from >= _cachedRangeMin) && (to is null || to <= _cachedRangeMax);
        if (isWithinCachedRange)
            return FilterByDeliveryDate(_allOrders, from, to).ToList();

        var orders = await Task.Run(() =>
            _odbcOrderRepositoryFactory(_settings).GetByDeliveryDateRange(from ?? _cachedRangeMin, to ?? _cachedRangeMax).ToList());

        var calculator = new BusinessDayCalculator(_holidays, _settings.DayMinutes);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var displayNames = await SqliteProductDisplayNameRepository.GetAllDisplayNamesAsync();
        var leadDaysOverrides = await SqliteProductDisplayNameRepository.GetAllCompletionDateLeadDaysAsync();
        var dbDefs = (await new SqliteProcessDefinitionRepository().GetAllAsync()).ToList();

        OrderProcessBuildService.Build(orders, _odbcProcessDefinitions.ToList(), dbDefs, displayNames, leadDaysOverrides, _settings.CompletionDateLeadDays, calculator, today);

        return orders;
    }

    private static IEnumerable<Order> FilterByDeliveryDate(IEnumerable<Order> orders, DateOnly? from, DateOnly? to) {
        if (from is { } f) orders = orders.Where(o => o.DeliveryDate >= f);
        if (to is { } t) orders = orders.Where(o => o.DeliveryDate <= t);
        return orders;
    }

    private void SetLoading(bool isLoading) {
        BtnApply.IsEnabled = !isLoading;
        BtnClearRange.IsEnabled = !isLoading;
        LoadingProgressBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplySummary(DashboardSummary summary) {
        TxtTotalCount.Text = $"{summary.TotalCount}件";
        TxtCompletedCount.Text = $"{summary.CompletedCount}件（{summary.CompletedRateText}）";
        TxtOverdueCount.Text = $"{summary.OverdueCount}件";
        TxtWarningCount.Text = $"{summary.WarningCount}件";
        DepartmentGrid.ItemsSource = summary.DepartmentRows;
        TxtEmpty.Visibility = summary.DepartmentRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 集計対象が変わると選択済みセルが示す明細も古くなるため表示をリセットする
        TxtDetailTitle.Text = "セルを選択すると明細を表示します";
        ItemDetailList.ItemsSource = null;
    }

    private async void BtnApply_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void BtnClearRange_Click(object sender, RoutedEventArgs e) {
        ResetToDefaultRange();
        await RefreshAsync();
    }

    // 列（全工程数／完了工程数／残工程数／超過件数）ごとに対応する内訳一覧を出し分ける。
    // 部署名列を選択した場合は「残工程」を既定として表示する（対応が必要なものを優先的に見せるため）
    private void DepartmentGrid_CurrentCellChanged(object sender, EventArgs e) {
        if (DepartmentGrid.CurrentCell.Item is not DashboardDepartmentRow row || DepartmentGrid.CurrentCell.Column is not { } column) return;

        var columnIndex = DepartmentGrid.Columns.IndexOf(column);
        var (label, items) = columnIndex switch {
            1 => ("全工程", row.AllItems),
            2 => ("完了工程", row.CompletedItems),
            4 => ("超過", row.OverdueItems),
            _ => ("残工程", row.RemainingItems),
        };

        if (items.Count == 0) {
            TxtDetailTitle.Text = "対象の工程がありません";
            ItemDetailList.ItemsSource = null;
            return;
        }

        TxtDetailTitle.Text = $"{row.DepartmentName}　{label} {items.Count}件";
        ItemDetailList.ItemsSource = items;
    }

    private void ItemDetailList_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        if (ItemDetailList.SelectedItem is not DashboardProcessItem item) return;
        new OrderDetailWindow(item.Order, _settings.ShowRequiredTimeInMinutes, _settings.DayMinutes) { Owner = this }.ShowDialog();
    }
}
