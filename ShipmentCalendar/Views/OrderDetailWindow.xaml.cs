using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using System.Windows;

namespace ShipmentCalendar.Views;

public partial class OrderDetailWindow : Window {
    private readonly Order _order;
    private readonly bool _showRequiredTimeInMinutes;

    public OrderDetailWindow(Order order, bool showRequiredTimeInMinutes = false) {
        InitializeComponent();
        _order = order;
        _showRequiredTimeInMinutes = showRequiredTimeInMinutes;
        DataContext = order;
        DetailProcessBar.Processes = order.Processes;
        DetailProcessBar.ShowRequiredTimeInMinutes = showRequiredTimeInMinutes;
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
