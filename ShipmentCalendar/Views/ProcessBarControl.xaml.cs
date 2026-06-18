using ShipmentCalendar.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class ProcessBarControl : UserControl {
    // 日付バーの高さ（UpdateRowHeight で参照する）
    public const double DateBarHeight = 16;

    private static readonly Brush DateBarBackgroundBrush = CreateFrozenBrush(Color.FromRgb(220, 230, 241));
    private static readonly Brush DefaultBorderBrush     = CreateFrozenBrush(Color.FromRgb(154, 176, 204));
    private static readonly Brush OffsetBackgroundBrush  = CreateFrozenBrush(Color.FromArgb(90, 160, 160, 160));
    private static readonly Brush OffsetBorderBrush      = CreateFrozenBrush(Color.FromArgb(160, 120, 120, 120));

    private static SolidColorBrush CreateFrozenBrush(Color color) {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

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
        if (minDate == default || maxDate == default || minDate > maxDate || maxDate.DayNumber - minDate.DayNumber > 365) return;

        // 週末をスキップして営業日リストを生成（日付バー・工程バーで共有）
        var businessDays = new List<DateOnly>();
        for (var d = minDate; d <= maxDate; d = d.AddDays(1)) {
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                businessDays.Add(d);
        }

        // 全期間が週末のみ（例: 土〜日）の場合は描画不可
        if (businessDays.Count == 0) return;

        // 日付バー・工程バーで同一の列定義（1列=1営業日）を共有し、SetColumnSpanで整合を保つ
        foreach (var (date, col) in businessDays.Select((d, i) => (d, i))) {
            DateBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            BarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dateBorder = new Border {
                Background = DateBarBackgroundBrush,
                BorderBrush = DefaultBorderBrush,
                BorderThickness = new Thickness(1),
                Child = new TextBlock {
                    Text = date.ToString("M/d"),
                    FontSize = Math.Max(6, BarFontSize - 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.DimGray,
                }
            };
            Grid.SetColumn(dateBorder, col);
            DateBarGrid.Children.Add(dateBorder);
        }

        // 最初の工程の着手前をグレーで表示
        var firstStartCol = businessDays.IndexOf(Processes[0].StartDate);
        if (firstStartCol > 0) {
            var offsetBorder = new Border {
                Background = OffsetBackgroundBrush,
                BorderBrush = OffsetBorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                ToolTip = $"着手まで {firstStartCol}営業日",
            };
            Grid.SetColumn(offsetBorder, 0);
            Grid.SetColumnSpan(offsetBorder, firstStartCol);
            BarGrid.Children.Add(offsetBorder);
        }

        // 各工程を StartDate〜DueDate のセルスパンで配置
        foreach (var process in Processes) {
            var startCol = businessDays.IndexOf(process.StartDate);
            var endCol   = businessDays.IndexOf(process.DueDate);
            if (startCol < 0 || endCol < 0 || startCol > endCol) continue;
            var tooltip = $"{process.ProcessName}\n必要時間: {process.RequiredMinutes / 60.0:F1}h\n{process.StartDate:M/d} → {process.DueDate:M/d}";
            var border = new Border {
                Background = StatusToColorConverter.StatusToBrush(process.Status),
                BorderBrush = DefaultBorderBrush,
                BorderThickness = new Thickness(1),
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
            Grid.SetColumnSpan(border, endCol - startCol + 1);
            BarGrid.Children.Add(border);
        }
    }
}
