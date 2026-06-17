using ShipmentCalendar.Models;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class OrderDetailWindow : Window {
    public OrderDetailWindow(Order order) {
        InitializeComponent();
        TxtProductName.Text = order.ProductName;
        TxtOrderNumber.Text = order.OrderNumber;
        TxtPlannedQuantity.Text = order.PlannedQuantity.ToString();
        TxtItemNumber.Text = order.ItemNumber;
        TxtModelCode.Text = order.ModelCode;
        TxtManufactureNumber.Text = order.ManufactureNumber;
        TxtDates.Text = $"{order.DeliveryDate:yyyy/MM/dd} / {order.CompletionDate:yyyy/MM/dd}";
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
    }
}

/// <summary>ProcessStatusを日本語ラベルに変換するコンバーター</summary>
public class ProcessStatusToLabelConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is ProcessStatus status ? status switch {
            ProcessStatus.Completed => "完了",
            ProcessStatus.InProgress => "進行中",
            ProcessStatus.Warning => "警告",
            ProcessStatus.Overdue => "超過",
            ProcessStatus.NotStarted => "未着手",
            _ => ""
        } : "";

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}
