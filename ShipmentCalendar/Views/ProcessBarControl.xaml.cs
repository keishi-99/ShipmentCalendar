using ShipmentCalendar.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class ProcessBarControl : UserControl {
    // 日付バーの高さ（UpdateRowHeight で参照する）
    public const double DateBarHeight = 16;

    public static readonly DependencyProperty ProcessesProperty =
        DependencyProperty.Register(
            nameof(Processes),
            typeof(IReadOnlyList<OrderProcess>),
            typeof(ProcessBarControl),
            new PropertyMetadata(null, (d, _) => ((ProcessBarControl)d).RebuildBars()));

    public IReadOnlyList<OrderProcess> Processes {
        get => (IReadOnlyList<OrderProcess>)GetValue(ProcessesProperty);
        set => SetValue(ProcessesProperty, value);
    }

    public static readonly DependencyProperty BarFontSizeProperty =
        DependencyProperty.Register(
            nameof(BarFontSize),
            typeof(double),
            typeof(ProcessBarControl),
            new PropertyMetadata(10.0, (d, _) => ((ProcessBarControl)d).RebuildBars()));

    public double BarFontSize {
        get => (double)GetValue(BarFontSizeProperty);
        set => SetValue(BarFontSizeProperty, value);
    }

    public ProcessBarControl() {
        InitializeComponent();
    }

    private void RebuildBars() {
        DateBarGrid.ColumnDefinitions.Clear();
        DateBarGrid.Children.Clear();
        BarGrid.ColumnDefinitions.Clear();
        BarGrid.Children.Clear();
        if (Processes == null || Processes.Count == 0) return;

        var minDate = Processes.Min(p => p.StartDate);
        var maxDate = Processes.Max(p => p.DueDate);

        // 未設定日付や異常に広い範囲（365日超）はスキップ
        if (minDate == default || maxDate == default || minDate > maxDate || minDate.AddDays(365) < maxDate) return;

        // 週末をスキップして営業日リストを生成（日付バー・工程バーで共有）
        var businessDays = new List<DateOnly>();
        for (var d = minDate; d <= maxDate; d = d.AddDays(1)) {
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                businessDays.Add(d);
        }

        // 日付バー: 1列=480*（1営業日=480分相当）で統一することで工程バーと分単位で位置が合う
        foreach (var (date, col) in businessDays.Select((d, i) => (d, i))) {
            DateBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(480, GridUnitType.Star) });
            var border = new Border {
                Background = new SolidColorBrush(Color.FromRgb(220, 230, 241)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(0.5),
                Child = new TextBlock {
                    Text = date.ToString("M/d"),
                    FontSize = Math.Max(6, BarFontSize - 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.DimGray,
                }
            };
            Grid.SetColumn(border, col);
            DateBarGrid.Children.Add(border);
        }

        // 工程バー: 日付バーと同じ列構成（1日=480*）で各工程をStartDate~DueDateの列範囲に配置
        // これにより日付バーの目盛りと工程の位置が正確に一致する
        var dayIndex = businessDays.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);

        foreach (var _ in businessDays)
            BarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(480, GridUnitType.Star) });

        foreach (var process in Processes) {
            if (!dayIndex.TryGetValue(process.StartDate, out var startCol)) startCol = 0;
            if (!dayIndex.TryGetValue(process.DueDate, out var endCol)) endCol = businessDays.Count - 1;
            var span = Math.Max(1, endCol - startCol + 1);

            var tooltip = $"{process.ProcessName}\n必要時間: {process.RequiredMinutes / 60.0:F1}h\n{process.StartDate:M/d} → {process.DueDate:M/d}";
            var border = new Border {
                Background = StatusToColorConverter.StatusToBrush(process.Status),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(0.5),
                ToolTip = tooltip,
                Child = new TextBlock {
                    Text = process.ProcessName,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = BarFontSize,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = Brushes.Black,
                    Margin = new Thickness(2, 0, 2, 0),
                    ClipToBounds = true,
                }
            };
            Grid.SetColumn(border, startCol);
            Grid.SetColumnSpan(border, span);
            BarGrid.Children.Add(border);
        }
    }
}
