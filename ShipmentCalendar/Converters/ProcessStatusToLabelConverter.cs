using ShipmentCalendar.Models;
using System.Globalization;
using System.Windows.Data;

namespace ShipmentCalendar.Converters;

/// <summary>ProcessStatusを日本語ラベルに変換するコンバーター</summary>
public class ProcessStatusToLabelConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ProcessStatus status ? status switch {
            ProcessStatus.Completed => "完了",
            ProcessStatus.InProgress => "進行中",
            ProcessStatus.Warning => "警告",
            ProcessStatus.Overdue => "超過",
            ProcessStatus.NotStarted => "未着手",
            _ => ""
        } : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
