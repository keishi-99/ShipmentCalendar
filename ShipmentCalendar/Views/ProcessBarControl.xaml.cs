using ShipmentCalendar.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class ProcessBarControl : UserControl {
    // 日付バーの高さ（UpdateRowHeight で参照する）
    public const double DateBarHeight = 16;

    private static readonly Brush _defaultBorderBrush      = CreateFrozenBrush(Color.FromRgb(154, 176, 204));
    private static readonly Brush _offsetBackgroundBrush   = CreateFrozenBrush(Color.FromArgb(90, 160, 160, 160));
    private static readonly Brush _offsetBorderBrush       = CreateFrozenBrush(Color.FromArgb(160, 120, 120, 120));
    // クールタイム・外注待ち・日付バーの色はApp.xamlのリソースで一元管理（凡例・リスト表示と共有）
    private static Brush DwellTimeBrush      => (Brush)Application.Current.Resources["DwellTimeBrush"];
    private static Brush OutsourceLeadBrush => (Brush)Application.Current.Resources["OutsourceLeadBrush"];
    private static Brush DateBarBackgroundBrushA => (Brush)Application.Current.Resources["DateBarBackgroundBrushA"];
    private static Brush DateBarBackgroundBrushB => (Brush)Application.Current.Resources["DateBarBackgroundBrushB"];

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

    public static readonly DependencyProperty ShowRequiredTimeInMinutesProperty =
        DependencyProperty.Register(
            nameof(ShowRequiredTimeInMinutes),
            typeof(bool),
            typeof(ProcessBarControl),
            new PropertyMetadata(false, (d, _) => ((ProcessBarControl)d).RebuildBars()));

    /// <summary>必要時間のツールチップ表示単位。true=分表記、false=時間表記</summary>
    public bool ShowRequiredTimeInMinutes {
        get => (bool)GetValue(ShowRequiredTimeInMinutesProperty);
        set => SetValue(ShowRequiredTimeInMinutesProperty, value);
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

        // 日付バー: 1列=480*（1営業日=480分相当）で統一することで工程バーと分単位で位置が合う
        foreach (var (date, col) in businessDays.Select((d, i) => (d, i))) {
            DateBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(480, GridUnitType.Star) });
            var dateBorder = new Border {
                Background = col % 2 == 0 ? DateBarBackgroundBrushA : DateBarBackgroundBrushB,
                BorderBrush = _defaultBorderBrush,
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

        // 工程バー: 全工程を分単位のフラットタイムラインとして配置する。
        // 日付バーと同じ総スター幅（営業日数×480）を使うため、日付との整合が保たれる
        var totalDayMinutes = businessDays.Count * 480.0;

        // BusinessDayCalculator.BuildProcessesと同じ考え方で、末尾工程から逆向きに積み上げる。
        // 外注待ちのゲート（待機日数分の空白）は日付境界に固定し、工程自身の所要時間は
        // 分単位で正確に積むことで、外注待ち直前の工程がその日の終わりにぴったり収まる
        var segments = new List<Segment>();
        double pos = 0;
        double cumulativeRunningTime = 0;
        foreach (var process in Processes.AsEnumerable().Reverse()) {
            if (process.OutsourceLeadDays > 0) {
                var daysSoFar = (int)(cumulativeRunningTime / 480.0) + 1;
                var gate = (daysSoFar + process.OutsourceLeadDays) * 480.0;

                // 外注待ちが連続する等でposの丸め誤差が累積している場合、totalGapが
                // OutsourceLeadDays分の幅を下回ることがあるため、外注待ち分を優先的に
                // 確保し、余りをpreGapに割り当てる
                var totalGap = gate - pos;
                var outsourceMinutes = Math.Min(totalGap, process.OutsourceLeadDays * 480.0);
                var preGap = totalGap - outsourceMinutes;

                if (preGap >= 1) {
                    segments.Add(new Segment(SegmentKind.PreGap, preGap, process));
                    pos += preGap;
                }
                if (outsourceMinutes >= 1) {
                    segments.Add(new Segment(SegmentKind.Outsource, outsourceMinutes, process));
                    pos += outsourceMinutes;
                }
            }

            if (process.DwellTimeMinutes >= 1) {
                segments.Add(new Segment(SegmentKind.DwellTime, process.DwellTimeMinutes, process));
                pos += process.DwellTimeMinutes;
            }

            var procWidth = Math.Max(1, process.RequiredMinutes);
            segments.Add(new Segment(SegmentKind.Process, procWidth, process));
            pos += procWidth;

            cumulativeRunningTime += process.RequiredMinutes + process.OutsourceLeadDays * 480.0 + process.DwellTimeMinutes;
        }
        segments.Reverse();
        var initialOffset = Math.Max(0, totalDayMinutes - pos);

        int barCol = 0;

        // 初期オフセット（最初の工程の着手前の空き時間）をグレーで表示
        if (initialOffset >= 1) {
            BarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(initialOffset, GridUnitType.Star) });
            var offsetBorder = new Border {
                Background = _offsetBackgroundBrush,
                BorderBrush = _offsetBorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                ToolTip = $"着手まで {initialOffset / 60.0:F1}h",
            };
            Grid.SetColumn(offsetBorder, barCol++);
            BarGrid.Children.Add(offsetBorder);
        }

        foreach (var segment in segments) {
            BarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(segment.Width, GridUnitType.Star) });
            var border = CreateSegmentBorder(segment);
            Grid.SetColumn(border, barCol++);
            BarGrid.Children.Add(border);
        }
    }

    private Border CreateSegmentBorder(Segment segment) {
        var process = segment.Process;
        var requiredTimeText = process.GetRequiredTimeDescription(ShowRequiredTimeInMinutes);
        return segment.Kind switch {
            SegmentKind.Process => new Border {
                Background = StatusToColorConverter.StatusToBrush(process.Status),
                BorderBrush = _defaultBorderBrush,
                BorderThickness = new Thickness(1),
                ToolTip = $"{process.ProcessName}\n必要時間: {requiredTimeText}\n{process.StartDate:M/d} → {process.DueDate:M/d}",
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
            },
            SegmentKind.DwellTime => new Border {
                Background = DwellTimeBrush,
                BorderBrush = _defaultBorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                ToolTip = $"滞留時間 {process.DwellTimeMinutes / 60.0:F1}h",
            },
            SegmentKind.Outsource => new Border {
                Background = OutsourceLeadBrush,
                BorderBrush = _defaultBorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                ToolTip = $"外注待ち {process.OutsourceLeadDays}日",
            },
            SegmentKind.PreGap => new Border(), // 外注待ち前の空白（その日の残り時間）
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment.Kind, "未対応の SegmentKind です"),
        };
    }

    // 浮動小数点誤差（例: 960.0000000000001 % 480 ≈ 0 にならない）を許容しつつ
    // 現在位置から次の480分境界までの距離を返す。境界上にある場合は 0 を返す
    private static double GetDistanceToNextBoundary(double position) {
        var remainder = position % 480.0;
        if (remainder < 0.001 || remainder > 479.999) return 0;
        return 480.0 - remainder;
    }

    private enum SegmentKind { Process, DwellTime, PreGap, Outsource }

    private readonly record struct Segment(SegmentKind Kind, double Width, OrderProcess Process);
}
