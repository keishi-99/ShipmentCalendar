using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShipmentCalendar.Views;

public partial class ProcessBottleneckWindow : Window {
    private readonly AppSettings _settings;
    private List<(string Seiban, string ItemNumber, string ItemName, string ModelCode, string DestinationCode, DateOnly ActualDate, string WorkerName, double ActualWorkMinutes, int PlannedQuantity, DateOnly? DeliveryDate)> _completedRows = [];
    private IDictionary<string, List<ProcessDefinition>> _defsByItem = new Dictionary<string, List<ProcessDefinition>>();
    private List<Holiday> _holidays = [];
    private Dictionary<string, int> _leadDaysByItem = [];

    public ProcessBottleneckWindow(AppSettings settings) {
        InitializeComponent();
        _settings = settings;
        StartDatePicker.SelectedDate = DateTime.Today.AddDays(-90);
        EndDatePicker.SelectedDate = DateTime.Today;

        CmbOverMode.SelectedIndex = settings.BottleneckOverThresholdMode == BottleneckThresholdMode.Percent ? 0 : 1;
        CmbUnderMode.SelectedIndex = settings.BottleneckUnderThresholdMode == BottleneckThresholdMode.Percent ? 0 : 1;
        TxtOverValue.Text = settings.BottleneckOverThresholdValue.ToString(CultureInfo.InvariantCulture);
        TxtUnderValue.Text = settings.BottleneckUnderThresholdValue.ToString(CultureInfo.InvariantCulture);
    }

    // モード（割合／固定分）の切り替えに応じて、数値入力欄の単位表示を追従させる
    private void ThresholdMode_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (TxtOverUnit == null || TxtUnderUnit == null) return;
        TxtOverUnit.Text = CmbOverMode.SelectedIndex == 0 ? "%" : "分";
        TxtUnderUnit.Text = CmbUnderMode.SelectedIndex == 0 ? "%" : "分";
    }

    private bool TryReadThresholds(out BottleneckThresholdMode overMode, out double overValue, out BottleneckThresholdMode underMode, out double underValue) {
        overMode = CmbOverMode.SelectedIndex == 0 ? BottleneckThresholdMode.Percent : BottleneckThresholdMode.FixedMinutes;
        underMode = CmbUnderMode.SelectedIndex == 0 ? BottleneckThresholdMode.Percent : BottleneckThresholdMode.FixedMinutes;

        var overOk = double.TryParse(TxtOverValue.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out overValue) && double.IsFinite(overValue) && overValue >= 0;
        var underOk = double.TryParse(TxtUnderValue.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out underValue) && double.IsFinite(underValue) && underValue >= 0;
        return overOk && underOk;
    }

    private void BtnApplyThreshold_Click(object sender, RoutedEventArgs e) {
        if (!TryReadThresholds(out var overMode, out var overValue, out var underMode, out var underValue)) {
            MessageBox.Show("しきい値は0以上の数値で入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.BottleneckOverThresholdMode = overMode;
        _settings.BottleneckOverThresholdValue = overValue;
        _settings.BottleneckUnderThresholdMode = underMode;
        _settings.BottleneckUnderThresholdValue = underValue;
        AppSettingsService.Save(_settings);

        if (_completedRows.Count > 0) RunAggregate();
    }

    private void RunAggregate() {
        var result = ProcessBottleneckCalculator.Aggregate(_completedRows, _defsByItem,
            _settings.BottleneckOverThresholdMode, _settings.BottleneckOverThresholdValue,
            _settings.BottleneckUnderThresholdMode, _settings.BottleneckUnderThresholdValue);

        ResultGrid.ItemsSource = result;
        TxtStatus.Text = result.Count == 0 ? "該当する実績がありません" : $"{result.Count} 工程を表示";
    }

    private async void BtnSearch_Click(object sender, RoutedEventArgs e) {
        if (StartDatePicker.SelectedDate is not DateTime start || EndDatePicker.SelectedDate is not DateTime end || start > end) {
            MessageBox.Show("開始日・終了日を正しく指定してください", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var from = DateOnly.FromDateTime(start);
        var to = DateOnly.FromDateTime(end);

        BtnSearch.IsEnabled = false;
        SearchProgressBar.Visibility = Visibility.Visible;
        TxtStatus.Text = "検索中...";
        ResultGrid.ItemsSource = null;

        try {
            // ドリルダウンでOrderDetailWindowを開く際にOrderを再構築するために必要（検索のたびに1回だけ取得してキャッシュする）。
            // ODBC問い合わせと依存関係がないため、並行して実行し検索開始を遅らせないようにする
            var holidaysTask = new SqliteHolidayRepository().GetAllAsync();
            var leadDaysTask = SqliteProductDisplayNameRepository.GetAllCompletionDateLeadDaysAsync();
            var odbcTask = Task.Run(() => {
                var rows = new OdbcOrderRepository(_settings).GetCompletedProcessesByDateRange(from, to).ToList();
                var itemNumbers = rows.Select(r => r.ItemNumber).Distinct(StringComparer.OrdinalIgnoreCase);
                var defs = new OdbcProcessDefinitionRepository(_settings).GetByItemNumbers(itemNumbers);
                return (rows, defs);
            });

            await Task.WhenAll(holidaysTask, leadDaysTask, odbcTask);
            _holidays = (await holidaysTask).ToList();
            _leadDaysByItem = await leadDaysTask;
            var (completedRows, defsByItem) = await odbcTask;
            _completedRows = completedRows;
            _defsByItem = defsByItem;

            RunAggregate();
        } catch (Exception ex) {
            TxtStatus.Text = $"検索に失敗しました: {ex.Message}";
        } finally {
            BtnSearch.IsEnabled = true;
            SearchProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ResultGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (ResultGrid.SelectedItem is not ProcessBottleneckRow row || row.Items.Count == 0) {
            TxtDetailTitle.Text = "行を選択すると明細を表示します";
            ItemDetailList.ItemsSource = null;
            return;
        }

        TxtDetailTitle.Text = $"{row.ItemNumber}　{row.ProcessName}　{row.Items.Count}件";
        ItemDetailList.ItemsSource = row.Items;
    }

    private async void ItemDetailList_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        if (ItemDetailList.SelectedItem is not ProcessBottleneckItem item) return;
        await OpenOrderDetailAsync(item.Seiban);
    }

    // ドリルダウン明細の製番から、ProductPerformanceWindowと同じ手順でOrderDetailWindow用のOrderを組み立てる（休日も考慮する）
    private async Task OpenOrderDetailAsync(string seiban) {
        var matchedRows = _completedRows.Where(r => string.Equals(r.Seiban, seiban, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matchedRows.Count == 0) return;
        var first = matchedRows[0];
        if (!_defsByItem.TryGetValue(first.ItemNumber, out var defs) || defs.Count == 0) return;

        var calculator = new BusinessDayCalculator(_holidays, _settings.DayMinutes);
        var deliveryDate = first.DeliveryDate ?? DateOnly.FromDateTime(DateTime.Today);
        var leadDays = _leadDaysByItem.GetValueOrDefault(first.ItemNumber, _settings.CompletionDateLeadDays);

        var order = new Order {
            ProductName = first.ItemName,
            ItemNumber = first.ItemNumber,
            ModelCode = first.ModelCode,
            ManufactureNumber = first.Seiban,
            DeliveryDate = deliveryDate,
            CompletionDate = calculator.SubtractBusinessDays(deliveryDate, leadDays),
            PlannedQuantity = first.PlannedQuantity
        };

        var completedByDestNumber = matchedRows
            .GroupBy(r => r.DestinationCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => {
                    var latest = g.OrderByDescending(r => r.ActualDate).First();
                    return ((DateOnly? ActualDate, string WorkerName, double ActualWorkMinutes))(latest.ActualDate, latest.WorkerName, g.Sum(r => r.ActualWorkMinutes));
                },
                StringComparer.OrdinalIgnoreCase);

        order.Processes = calculator.BuildProcesses(order, defs.Where(d => d.IsVisible), completedByDestNumber);

        BusinessDayCalculator.MarkAllCompletedIfFinalReceiptDone(order.Processes, defs, completedByDestNumber);

        new OrderDetailWindow(order, _settings.ShowRequiredTimeInMinutes, _settings.DayMinutes) { Owner = this }.ShowDialog();
    }
}
