using ShipmentCalendar.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class ProcessBarControl : UserControl {
    // 日付バーの高さ（UpdateRowHeight で参照する）
    public const double DateBarHeight = 16;

    private static readonly Brush DateBarBackgroundBrush  = CreateFrozenBrush(Color.FromRgb(220, 230, 241));
    private static readonly Brush DefaultBorderBrush      = CreateFrozenBrush(Color.FromRgb(154, 176, 204));
    private static readonly Brush OffsetBackgroundBrush   = CreateFrozenBrush(Color.FromArgb(90, 160, 160, 160));
    private static readonly Brush OffsetBorderBrush       = CreateFrozenBrush(Color.FromArgb(160, 120, 120, 120));
    // クールタイム・外注待ちの色はApp.xamlのリソースで一元管理（凡例・リスト表示と共有）
    private static Brush DwellTimeBrush      => (Brush)Application.Current.Resources["DwellTimeBrush"];
    private static Brush OutsourceLeadBrush => (Brush)Application.Current.Resources["OutsourceLeadBrush"];

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

        // 工程バー: 全工程を分単位のフラットタイムラインとして連続配置する
        // 日付バーと同じ総スター幅（営業日数×480）を使うため、日付との整合が保たれる
        var totalDayMinutes = businessDays.Count * 480.0;

        // セグメント（工程・クールタイム・外注待ち前のギャップ・外注待ち）を1回の走査で構築する。
        // 外注待ちの開始位置は pre-gap で日付境界に揃うが、終了位置は外注待ち後に続く
        // 工程の幅次第でずれるため、後ろから走査して post-gap を挿入し後続全体を480の倍数に揃える
        var rawSegments = new List<Segment>();
        double pos = 0;
        foreach (var process in Processes) {
            var procWidth = Math.Max(1, process.RequiredMinutes);
            rawSegments.Add(new Segment(SegmentKind.Process, procWidth, process));
            pos += procWidth;

            if (process.DwellTimeMinutes >= 1) {
                rawSegments.Add(new Segment(SegmentKind.DwellTime, process.DwellTimeMinutes, process));
                pos += process.DwellTimeMinutes;
            }

            if (process.OutsourceLeadDays > 0) {
                var preGap = GetDistanceToNextBoundary(pos);
                if (preGap >= 1) {
                    rawSegments.Add(new Segment(SegmentKind.PreGap, preGap, process));
                    pos += preGap;
                }
                var outsourceMinutes = process.OutsourceLeadDays * 480.0;
                rawSegments.Add(new Segment(SegmentKind.Outsource, outsourceMinutes, process));
                pos += outsourceMinutes;
            }
        }

        var segments = new List<Segment>();
        double trailingWidth = 0;
        for (int i = rawSegments.Count - 1; i >= 0; i--) {
            var seg = rawSegments[i];
            if (seg.Kind == SegmentKind.Outsource) {
                var postGap = GetDistanceToNextBoundary(trailingWidth);
                if (postGap >= 1) {
                    segments.Insert(0, new Segment(SegmentKind.PostGap, postGap, seg.Process));
                    trailingWidth += postGap;
                }
            }
            segments.Insert(0, seg);
            trailingWidth += seg.Width;
        }
        var initialOffset = Math.Max(0, totalDayMinutes - trailingWidth);

        int barCol = 0;

        // 初期オフセット（最初の工程の着手前の空き時間）をグレーで表示
        if (initialOffset >= 1) {
            BarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(initialOffset, GridUnitType.Star) });
            var offsetBorder = new Border {
                Background = OffsetBackgroundBrush,
                BorderBrush = OffsetBorderBrush,
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
        var requiredTimeText = ShowRequiredTimeInMinutes
            ? $"{process.RequiredMinutes:F0}分"
            : $"{process.RequiredMinutes / 60.0:F1}h";
        return segment.Kind switch {
            SegmentKind.Process => new Border {
                Background = StatusToColorConverter.StatusToBrush(process.Status),
                BorderBrush = DefaultBorderBrush,
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
                BorderBrush = DefaultBorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                ToolTip = $"滞留時間 {process.DwellTimeMinutes / 60.0:F1}h",
            },
            SegmentKind.Outsource => new Border {
                Background = OutsourceLeadBrush,
                BorderBrush = DefaultBorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                ToolTip = $"外注待ち {process.OutsourceLeadDays}日",
            },
            SegmentKind.PreGap or SegmentKind.PostGap => new Border(), // 外注待ち前後の空白（その日の残り時間）
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

    private enum SegmentKind { Process, DwellTime, PreGap, Outsource, PostGap }

    private readonly record struct Segment(SegmentKind Kind, double Width, OrderProcess Process);
}
