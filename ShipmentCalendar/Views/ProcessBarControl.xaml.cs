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
        RebuildDateBar();
        RebuildProcessBar();
    }

    private void RebuildDateBar() {
        DateBarGrid.ColumnDefinitions.Clear();
        DateBarGrid.Children.Clear();
        if (Processes == null || Processes.Count == 0) return;

        var minDate = Processes.Min(p => p.StartDate);
        var maxDate = Processes.Max(p => p.DueDate);

        // 週末をスキップして営業日リストを生成
        var businessDays = new List<DateOnly>();
        for (var d = minDate; d <= maxDate; d = d.AddDays(1)) {
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                businessDays.Add(d);
        }

        foreach (var (date, col) in businessDays.Select((d, i) => (d, i))) {
            DateBarGrid.ColumnDefinitions.Add(new ColumnDefinition {
                Width = new GridLength(1, GridUnitType.Star)
            });
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
    }

    private void RebuildProcessBar() {
        BarGrid.ColumnDefinitions.Clear();
        BarGrid.Children.Clear();
        if (Processes == null || Processes.Count == 0) return;

        for (int i = 0; i < Processes.Count; i++) {
            var process = Processes[i];
            var width = process.RequiredMinutes > 0 ? process.RequiredMinutes : 1.0;
            BarGrid.ColumnDefinitions.Add(new ColumnDefinition {
                Width = new GridLength(width, GridUnitType.Star)
            });
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
            Grid.SetColumn(border, i);
            BarGrid.Children.Add(border);
        }
    }
}
