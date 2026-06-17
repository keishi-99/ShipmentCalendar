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

        // 日付バー・工程バーとも同じ列構造（1列=1営業日）
        foreach (var _ in businessDays) {
            DateBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            BarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        // 日付バー
        foreach (var (date, col) in businessDays.Select((d, i) => (d, i))) {
            var border = new Border {
                Background = new SolidColorBrush(Color.FromRgb(220, 230, 241)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(0.5),
                Child = new TextBlock {
                    Text = date.ToString("M/d"),
                    FontSize = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.DimGray,
                }
            };
            Grid.SetColumn(border, col);
            DateBarGrid.Children.Add(border);
        }

        // 工程バー（StartDate〜DueDateを日付グリッドに対応させてColumnSpanで配置）
        foreach (var process in Processes) {
            var startCol = businessDays.FindIndex(d => d >= process.StartDate);
            var endCol = businessDays.FindLastIndex(d => d <= process.DueDate);
            if (startCol < 0) startCol = 0;
            if (endCol < 0) endCol = startCol;
            var span = Math.Max(1, endCol - startCol + 1);

            var border = new Border {
                Background = StatusToColorConverter.StatusToBrush(process.Status),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(0.5),
                Child = new TextBlock {
                    Text = process.ProcessName,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = 10,
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
