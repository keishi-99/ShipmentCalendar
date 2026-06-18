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
    private static readonly Brush CoolTimeBrush           = CreateFrozenBrush(Color.FromRgb(200, 230, 201));
    private static readonly Brush OutsourceLeadBrush      = CreateFrozenBrush(Color.FromRgb(248, 187, 208));

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

        // Math.Maxや閾値による端数が出るため、実際にグリッドへ追加するスター幅を先算して整合させる
        double nonOffsetStars = 0;
        foreach (var p in Processes) {
            nonOffsetStars += Math.Max(1, p.RequiredMinutes);
            var gap = p.CoolTimeMinutes + p.OutsourceLeadDays * 480.0;
            if (gap >= 1) nonOffsetStars += gap;
        }
        var initialOffset = Math.Max(0, totalDayMinutes - nonOffsetStars);

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

        foreach (var process in Processes) {
            BarGrid.ColumnDefinitions.Add(new ColumnDefinition {
                Width = new GridLength(Math.Max(1, process.RequiredMinutes), GridUnitType.Star)
            });
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
            Grid.SetColumn(border, barCol++);
            BarGrid.Children.Add(border);

            // クールタイムを色付き列として挿入
            if (process.CoolTimeMinutes >= 1) {
                BarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(process.CoolTimeMinutes, GridUnitType.Star) });
                var coolBorder = new Border {
                    Background = CoolTimeBrush,
                    BorderBrush = DefaultBorderBrush,
                    BorderThickness = new Thickness(0, 0, 1, 0),
                    ToolTip = $"クールタイム {process.CoolTimeMinutes / 60.0:F1}h",
                };
                Grid.SetColumn(coolBorder, barCol++);
                BarGrid.Children.Add(coolBorder);
            }
            // 外注待ちを色付き列として挿入
            var outsourceMinutes = process.OutsourceLeadDays * 480.0;
            if (outsourceMinutes >= 1) {
                BarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(outsourceMinutes, GridUnitType.Star) });
                var outsourceBorder = new Border {
                    Background = OutsourceLeadBrush,
                    BorderBrush = DefaultBorderBrush,
                    BorderThickness = new Thickness(0, 0, 1, 0),
                    ToolTip = $"外注待ち {process.OutsourceLeadDays}日",
                };
                Grid.SetColumn(outsourceBorder, barCol++);
                BarGrid.Children.Add(outsourceBorder);
            }
        }
    }
}
