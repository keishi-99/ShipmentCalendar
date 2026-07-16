using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using System.Windows;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class OrderDetailWindow : Window {
    private const double LaneBarMaxSize = 200.0;

    private readonly Order _order;
    private readonly bool _showRequiredTimeInMinutes;

    public OrderDetailWindow(Order order, bool showRequiredTimeInMinutes = false, int dayMinutes = 420) {
        InitializeComponent();
        _order = order;
        _showRequiredTimeInMinutes = showRequiredTimeInMinutes;
        DataContext = order;
        DetailProcessBar.Processes = order.Processes;
        DetailProcessBar.ShowRequiredTimeInMinutes = showRequiredTimeInMinutes;
        DetailProcessBar.DayMinutes = dayMinutes;
        LaneList.ItemsSource = BuildLanes(order.Processes);
        Loaded += async (_, _) => await LoadProcessRowsAsync();
    }

    // 部署マスタをDBから取得し、工程一覧に担当部署名を付加して表示する
    // DB接続エラー等で例外が出ても画面表示自体は継続させるため、失敗時は部署名なしにフォールバックする
    private async Task LoadProcessRowsAsync() {
        var departmentNames = new Dictionary<int, string>();
        try {
            departmentNames = (await SqliteDepartmentRepository.GetAllAsync())
                .ToDictionary(d => d.Id, d => d.Name);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"部署マスタの取得に失敗しました: {ex.Message}");
        }
        ProcessGrid.ItemsSource = _order.Processes
            .OrderBy(p => p.SortOrder)
            .Select(p => new ProcessRow(p, _showRequiredTimeInMinutes, departmentNames.GetValueOrDefault(p.DepartmentId, "")))
            .ToList();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleLaneView_Checked(object sender, RoutedEventArgs e) {
        DetailProcessBar.Visibility = Visibility.Collapsed;
        LaneList.Visibility = Visibility.Visible;
    }

    private void ToggleLaneView_Unchecked(object sender, RoutedEventArgs e) {
        DetailProcessBar.Visibility = Visibility.Visible;
        LaneList.Visibility = Visibility.Collapsed;
    }

    // 工程ごとに標準（RequiredMinutes）を背景バー、実績（ActualWorkMinutes）を重ねたバーとして表示するレーンを作る
    // （製品別実績分析ウィンドウのBuildLanesと同じ考え方）
    private static List<DetailProcessLane> BuildLanes(IEnumerable<OrderProcess> processes) {
        return processes
            .OrderBy(p => p.SortOrder)
            .Select(p => {
                var withinStandardSize = p.RequiredMinutes > 0
                    ? Math.Min(p.ActualWorkMinutes, p.RequiredMinutes) / p.RequiredMinutes * LaneBarMaxSize
                    : 0.0;
                // 標準0分・実績超過分は標準の200%を上限にする（超過の正確な値は数値ラベル側で確認できる）
                var overflowSize = p.RequiredMinutes > 0
                    ? Math.Min(LaneBarMaxSize, Math.Max(0, p.ActualWorkMinutes - p.RequiredMinutes) / p.RequiredMinutes * LaneBarMaxSize)
                    : p.ActualWorkMinutes > 0 ? LaneBarMaxSize : 0.0;
                return new DetailProcessLane(p.ProcessName, p.RequiredMinutes, p.ActualWorkMinutes, withinStandardSize, overflowSize, p.WorkerName);
            })
            .ToList();
    }

    private record DetailProcessLane(string ProcessName, double StandardMinutes, double ActualMinutes, double ActualWithinStandardSize, double ActualOverflowSize, string WorkerName) {
        public string ActualTooltip => string.IsNullOrEmpty(WorkerName)
            ? $"実績 {ActualMinutes:F1}分 / 標準 {StandardMinutes:F1}分"
            : $"実績 {ActualMinutes:F1}分 / 標準 {StandardMinutes:F1}分\n担当: {WorkerName}";

        public string ActualMinutesText => $"{ActualMinutes:F1}分";
        public string StandardComparisonText => $" / {StandardMinutes:F1}分";
        public Brush ActualTextBrush => ActualOverflowSize > 0 ? new SolidColorBrush(Color.FromRgb(0x7A, 0x1A, 0x1A)) : Brushes.Black;
    }

    // DataGrid行用のラッパー（表示用テキストを付加する）
    private record ProcessRow(OrderProcess Process, bool ShowRequiredTimeInMinutes, string DepartmentName) {
        public int SortOrder => Process.SortOrder;
        public string ProcessName => Process.ProcessName;
        public string DestinationCode => Process.DestinationCode;
        public DateOnly StartDate => Process.StartDate;
        public DateOnly DueDate => Process.DueDate;
        public DateOnly? ActualDate => Process.ActualDate;
        public string WorkerName => Process.WorkerName;
        public ProcessStatus Status => Process.Status;
        public string RequiredHoursText => Process.GetRequiredTimeDescription(ShowRequiredTimeInMinutes);
        public string ActualWorkHoursText => Process.GetActualWorkTimeDescription(ShowRequiredTimeInMinutes);
        public string OutsourceLeadDaysText => Process.OutsourceLeadDays > 0
            ? $"{Process.OutsourceLeadDays}日"
            : "-";
        public string DwellTimeText => Process.DwellTimeMinutes > 0
            ? $"{Process.DwellTimeMinutes / 60.0:F1}h"
            : "-";
        public string WarningDaysText => Process.WarningDaysBeforeDeadline > 0
            ? $"{Process.WarningDaysBeforeDeadline}日前"
            : "-";
    }
}
