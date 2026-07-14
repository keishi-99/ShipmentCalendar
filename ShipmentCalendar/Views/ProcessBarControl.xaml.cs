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
    // 相対日数ルーラー（n日目）の上下端を強調し、上下に並ぶ標準/実績バーとの境界を分かりやすくする
    private static readonly Brush _rulerEmphasisBorderBrush = CreateFrozenBrush(Color.FromRgb(90, 90, 90));
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

    public static readonly DependencyProperty ShowDateBarProperty =
        DependencyProperty.Register(
            nameof(ShowDateBar),
            typeof(bool),
            typeof(ProcessBarControl),
            new PropertyMetadata(true, (d, _) => ((ProcessBarControl)d).RebuildBars()));

    /// <summary>ヘッダー行に実際の暦日付を表示するか。falseの場合、日付とは無関係な実績バー等のために、
    /// 480分（1営業日相当）ごとに区切った相対的な「n日目」ラベルを代わりに表示する</summary>
    public bool ShowDateBar {
        get => (bool)GetValue(ShowDateBarProperty);
        set => SetValue(ShowDateBarProperty, value);
    }

    public static readonly DependencyProperty TotalMinutesOverrideProperty =
        DependencyProperty.Register(
            nameof(TotalMinutesOverride),
            typeof(double?),
            typeof(ProcessBarControl),
            new PropertyMetadata(null, (d, _) => ((ProcessBarControl)d).RebuildBars()));

    /// <summary>ShowDateBar=falseのときの全幅（分）を明示的に指定する。未指定（null）の場合は自分自身の合計時間を使う。
    /// 複数のProcessBarControlを並べて比較する場合、同じ値を指定することで「1時間」の幅を揃えられる</summary>
    public double? TotalMinutesOverride {
        get => (double?)GetValue(TotalMinutesOverrideProperty);
        set => SetValue(TotalMinutesOverrideProperty, value);
    }

    public static readonly DependencyProperty SuppressHeaderRowProperty =
        DependencyProperty.Register(
            nameof(SuppressHeaderRow),
            typeof(bool),
            typeof(ProcessBarControl),
            new PropertyMetadata(false, (d, _) => ((ProcessBarControl)d).RebuildBars()));

    /// <summary>trueの場合、ヘッダー行（日付/相対日数ルーラー）自体を完全に非表示にする。
    /// 複数のProcessBarControlを縦に並べ、ヘッダーを1つだけ共有表示したい場合に使う</summary>
    public bool SuppressHeaderRow {
        get => (bool)GetValue(SuppressHeaderRowProperty);
        set => SetValue(SuppressHeaderRowProperty, value);
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

        RootGrid.RowDefinitions[0].Height = new GridLength(SuppressHeaderRow ? 0 : DateBarHeight);
        DateBarGrid.Visibility = SuppressHeaderRow ? Visibility.Collapsed : Visibility.Visible;

        // BusinessDayCalculator.BuildProcessesと同じ考え方で、末尾工程から逆向きに積み上げる。
        // 外注待ちのゲート（待機日数分の空白）は日付境界に固定し、工程自身の所要時間は
        // 分単位で正確に積むことで、外注待ち直前の工程がその日の終わりにぴったり収まる
        var segments = new List<Segment>();
        double pos = 0;
        for (int i = Processes.Count - 1; i >= 0; i--) {
            var process = Processes[i];
            if (process.OutsourceLeadDays > 0) {
                // daysSoFarはpos（後続工程が実際に消費した位置）を基準にする。外注待ちが
                // 複数回連続する場合、前回の待機による丸め分（繰り越し）も含める必要がある。
                // posがちょうど480の倍数の場合、floor+1だと1日多く繰り上がってしまうため
                // Ceilingで判定する。pos=0（末尾工程自体が外注待ち）の場合はCeiling(0/480)=0
                // となってしまうため、1日目として扱うために1に補正する
                var daysSoFar = pos > 0 ? (int)Math.Ceiling(pos / 480.0) : 1;
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
        }
        segments.Reverse();

        double totalDayMinutes;
        if (SuppressHeaderRow) {
            // ヘッダー行自体を非表示にする場合も、全幅の基準はTotalMinutesOverride（未指定ならpos）に揃える
            totalDayMinutes = TotalMinutesOverride ?? pos;
        } else if (ShowDateBar) {
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

            // 工程バー: 日付バーと同じ総スター幅（営業日数×480）を使うため、日付との整合が保たれる
            totalDayMinutes = businessDays.Count * 480.0;
        } else {
            // 実カレンダーの日付とは無関係な実績バー等のための表示。TotalMinutesOverride指定時はそちらを、
            // 未指定なら実績時間の合計(pos)を全幅とし、480分（1営業日相当）ごとに区切った「n日目」ラベルを表示する。
            // 実日付ではなく相対的な日数の目安として使う
            totalDayMinutes = TotalMinutesOverride ?? pos;
            var dayCount = Math.Max(1, (int)Math.Ceiling(totalDayMinutes / 480.0));
            var remaining = totalDayMinutes;
            for (int day = 0; day < dayCount && remaining >= 1; day++) {
                var width = Math.Min(480.0, remaining);
                DateBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
                var dateBorder = new Border {
                    Background = day % 2 == 0 ? DateBarBackgroundBrushA : DateBarBackgroundBrushB,
                    BorderBrush = _rulerEmphasisBorderBrush,
                    BorderThickness = new Thickness(1, 3, 1, 3),
                    Child = new TextBlock {
                        Text = $"{day + 1}日目",
                        FontSize = Math.Max(6, BarFontSize - 2),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.DimGray,
                    }
                };
                Grid.SetColumn(dateBorder, day);
                DateBarGrid.Children.Add(dateBorder);
                remaining -= width;
            }
        }
        var initialOffset = Math.Max(0, totalDayMinutes - pos);

        int barCol = 0;

        // 空き時間（スケール上の余り）をグレーで表示する。ShowDateBar=true（締切から逆算する計画バー）では
        // 着手前の空き時間として左側（先頭）に、ShowDateBar=false（日付と無関係な実績/標準バー）では
        // 中身を左端から詰めたいので余りを右側（末尾）に配置する
        void AddOffsetColumn(string toolTipLabel) {
            if (initialOffset < 1) return;
            BarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(initialOffset, GridUnitType.Star) });
            var offsetBorder = new Border {
                Background = _offsetBackgroundBrush,
                BorderBrush = _offsetBorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                ToolTip = $"{toolTipLabel} {initialOffset / 60.0:F1}h",
            };
            Grid.SetColumn(offsetBorder, barCol++);
            BarGrid.Children.Add(offsetBorder);
        }

        if (ShowDateBar) AddOffsetColumn("着手まで");

        foreach (var segment in segments) {
            BarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(segment.Width, GridUnitType.Star) });
            var border = CreateSegmentBorder(segment);
            Grid.SetColumn(border, barCol++);
            BarGrid.Children.Add(border);
        }

        if (!ShowDateBar) AddOffsetColumn("残り");
    }

    private Border CreateSegmentBorder(Segment segment) {
        var process = segment.Process;
        var requiredTimeText = process.GetRequiredTimeDescription(ShowRequiredTimeInMinutes);
        return segment.Kind switch {
            SegmentKind.Process => new Border {
                Background = StatusToColorConverter.StatusToBrush(process.Status),
                BorderBrush = _defaultBorderBrush,
                BorderThickness = new Thickness(1),
                ToolTip = $"{process.ProcessName}\n必要時間: {requiredTimeText}\n{process.StartDate:M/d} → {process.DueDate:M/d}"
                    + (string.IsNullOrEmpty(process.WorkerName) ? "" : $"\n担当: {process.WorkerName}"),
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

    private enum SegmentKind { Process, DwellTime, PreGap, Outsource }

    private readonly record struct Segment(SegmentKind Kind, double Width, OrderProcess Process);
}
