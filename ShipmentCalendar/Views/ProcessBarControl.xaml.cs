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

        // 工程バー: RequiredMinutes単位のStarで配置し、日付バーと分単位でスケールを合わせる
        // 逆算スケジュールのため最初の工程は日中から始まる場合があり、初期オフセットを空白列で表現する
        var totalDayMinutes = businessDays.Count * 480.0;
        var totalProcessMinutes = Processes.Sum(p => p.RequiredMinutes + p.CoolTimeMinutes + p.OutsourceLeadDays * 480.0);
        var initialOffset = Math.Max(0, totalDayMinutes - totalProcessMinutes);

        int barCol = 0;
        if (initialOffset > 0) {
            BarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(initialOffset, GridUnitType.Star) });
            // 初期オフセット（空き時間）を薄いハッチング背景で表現
            var offsetBorder = new Border {
                Background = new SolidColorBrush(Color.FromArgb(90, 160, 160, 160)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(160, 120, 120, 120)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                ToolTip = "空き時間",
            };
            Grid.SetColumn(offsetBorder, barCol++);
            BarGrid.Children.Add(offsetBorder);
        }

        foreach (var process in Processes) {
            BarGrid.ColumnDefinitions.Add(new ColumnDefinition {
                Width = new GridLength(Math.Max(1, process.RequiredMinutes), GridUnitType.Star)
            });
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
            Grid.SetColumn(border, barCol++);
            BarGrid.Children.Add(border);

            // クールタイム・外注待ちは空白列として挿入
            var gapMinutes = process.CoolTimeMinutes + process.OutsourceLeadDays * 480.0;
            if (gapMinutes > 0) {
                BarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gapMinutes, GridUnitType.Star) });
                barCol++;
            }
        }
    }
}
