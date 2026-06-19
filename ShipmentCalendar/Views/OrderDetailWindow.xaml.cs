using ShipmentCalendar.Models;
using System.Windows;

namespace ShipmentCalendar.Views;

public partial class OrderDetailWindow : Window {
    public OrderDetailWindow(Order order) {
        InitializeComponent();
        DataContext = order;
        DetailProcessBar.Processes = order.Processes;
        ProcessGrid.ItemsSource = order.Processes
            .OrderBy(p => p.SortOrder)
            .Select(p => new ProcessRow(p))
            .ToList();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // DataGrid行用のラッパー（表示用テキストを付加する）
    private record ProcessRow(OrderProcess Process) {
        public int SortOrder => Process.SortOrder;
        public string ProcessName => Process.ProcessName;
        public string DestinationCode => Process.DestinationCode;
        public DateOnly StartDate => Process.StartDate;
        public DateOnly DueDate => Process.DueDate;
        public DateOnly? ActualDate => Process.ActualDate;
        public ProcessStatus Status => Process.Status;
        public string RequiredHoursText => Process.RequiredMinutes > 0
            ? $"{Process.RequiredMinutes / 60.0:F1}h"
            : "-";
        public string OutsourceLeadDaysText => Process.OutsourceLeadDays > 0
            ? $"{Process.OutsourceLeadDays}日"
            : "-";
        public string CoolTimeText => Process.CoolTimeMinutes > 0
            ? $"{Process.CoolTimeMinutes / 60.0:F1}h"
            : "-";
        public string WarningDaysText => Process.WarningDaysBeforeDeadline > 0
            ? $"{Process.WarningDaysBeforeDeadline}日前"
            : "-";
    }
}
