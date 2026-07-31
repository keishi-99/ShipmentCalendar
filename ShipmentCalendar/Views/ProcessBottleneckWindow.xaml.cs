using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

            var result = ProcessBottleneckCalculator.Aggregate(completedRows, defsByItem);

            ResultGrid.ItemsSource = result;
            TxtStatus.Text = result.Count == 0 ? "該当する実績がありません" : $"{result.Count} 工程を表示";
        } catch (Exception ex) {
            TxtStatus.Text = $"検索に失敗しました: {ex.Message}";
        } finally {
            BtnSearch.IsEnabled = true;
            SearchProgressBar.Visibility = Visibility.Collapsed;
        }
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

        ItemDetailPopup.IsOpen = false;

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

    private void ResultGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not { Item: ProcessBottleneckRow row } target) return;
        if (row.Items.Count == 0) return;

        TxtPopupTitle.Text = $"{row.ItemNumber}　{row.ProcessName}　{row.Items.Count}件";
        ItemDetailList.ItemsSource = row.Items;
        ItemDetailPopup.PlacementTarget = target;
        ItemDetailPopup.IsOpen = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject {
        while (current != null) {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // StaysOpen=Falseだと、ポップアップを開いたダブルクリック自身のMouseUpが「外側クリック」と
    // 誤認されて即閉じてしまうため、StaysOpen=Trueにして次の新しいMouseDownでのみ閉じるようにする
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
        if (!ItemDetailPopup.IsOpen) return;
        if (ItemDetailPopup.Child is FrameworkElement content && e.OriginalSource is DependencyObject source && IsDescendant(source, content)) return;
        ItemDetailPopup.IsOpen = false;
    }

    private static bool IsDescendant(DependencyObject? element, DependencyObject ancestor) {
        while (element != null) {
            if (ReferenceEquals(element, ancestor)) return true;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
}
