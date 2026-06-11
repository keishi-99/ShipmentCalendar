using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using ShipmentCalendar.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class MainWindow : Window {
    private readonly MainViewModel _viewModel;

    public MainWindow() {
        InitializeComponent();
        _viewModel = new MainViewModel(
            new SqliteHolidayRepository(),
            new AppSettingsService());
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.LoadOrdersAsync();
        _viewModel.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(_viewModel.Orders))
                BuildProcessColumns();
        };
        UpdateDueDateDisplayButtonText();
        UpdateSortModeButtonText();
    }

    /// <summary>表示日切り替えボタンの文言を現在の設定に合わせて更新する</summary>
    private void UpdateDueDateDisplayButtonText() {
        BtnToggleDueDateDisplay.Content = _viewModel.Settings.ShowDueDateForNotStarted
            ? "表示中：完了必須日"
            : "表示中：着手必須日";
    }

    /// <summary>並び順切り替えボタンの文言を現在の設定に合わせて更新する</summary>
    private void UpdateSortModeButtonText() {
        BtnToggleSortMode.Content = _viewModel.Settings.SortByProcessDeadline
            ? "並び順：工程期限"
            : "並び順：出荷日";
    }

    private void BtnToggleDueDateDisplay_Click(object sender, RoutedEventArgs e) {
        _viewModel.Settings.ShowDueDateForNotStarted = !_viewModel.Settings.ShowDueDateForNotStarted;
        UpdateDueDateDisplayButtonText();
        _viewModel.SaveSettings();
        _viewModel.ApplyFilter();
    }

    private void BtnToggleSortMode_Click(object sender, RoutedEventArgs e) {
        _viewModel.ToggleSortMode();
        UpdateSortModeButtonText();
    }

    /// <summary>工程列をインデックスベースで動的生成する（列ヘッダー: 1, 2, 3...）</summary>
    private void BuildProcessColumns() {
        // 固定列（出荷日・完了日・品目番号・品目名・製番・計画数）以外を削除
        while (OrderGrid.Columns.Count > 6)
            OrderGrid.Columns.RemoveAt(6);

        if (!_viewModel.Orders.Any()) return;

        // 全注文中の最大工程数を列数とする
        var maxProcessCount = _viewModel.Orders.Max(o => o.Processes.Count);
        if (maxProcessCount == 0) return;

        for (int i = 0; i < maxProcessCount; i++) {
            var index = i;
            var column = new DataGridTemplateColumn {
                Header = (index + 1).ToString(),
                Width = 110
            };

            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.MarginProperty, new Thickness(1));

            // インデックスで工程を検索して背景色を設定
            var colorBinding = new MultiBinding { Converter = new ProcessIndexToStatusColorConverter() };
            colorBinding.Bindings.Add(new Binding("Processes"));
            colorBinding.Bindings.Add(new Binding() { Source = index });
            factory.SetBinding(Border.BackgroundProperty, colorBinding);

            // Grid（左: 工程名+期限日、右: 外注待ちギャップ表示）
            var gridFactory = new FrameworkElementFactory(typeof(Grid));
            var mainColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            mainColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var gapColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            gapColumn.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            gridFactory.AppendChild(mainColumn);
            gridFactory.AppendChild(gapColumn);

            // StackPanel（工程名 + 期限日を縦に並べる）
            var stackFactory = new FrameworkElementFactory(typeof(StackPanel));
            stackFactory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            stackFactory.SetValue(StackPanel.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            stackFactory.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
            stackFactory.SetValue(Grid.ColumnProperty, 0);

            // 工程名テキスト
            var nameFactory = new FrameworkElementFactory(typeof(TextBlock));
            var nameBinding = new MultiBinding { Converter = new ProcessIndexToNameConverter() };
            nameBinding.Bindings.Add(new Binding("Processes"));
            nameBinding.Bindings.Add(new Binding() { Source = index });
            nameFactory.SetBinding(TextBlock.TextProperty, nameBinding);
            nameFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            nameFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
            nameFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);

            // 期限日テキスト
            var dateFactory = new FrameworkElementFactory(typeof(TextBlock));
            var dateBinding = new MultiBinding { Converter = new ProcessIndexToDueDateConverter(_viewModel.Settings.ShowDueDateForNotStarted) };
            dateBinding.Bindings.Add(new Binding("Processes"));
            dateBinding.Bindings.Add(new Binding() { Source = index });
            dateFactory.SetBinding(TextBlock.TextProperty, dateBinding);
            dateFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            dateFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            dateFactory.SetValue(TextBlock.FontSizeProperty, 11.0);

            stackFactory.AppendChild(nameFactory);
            stackFactory.AppendChild(dateFactory);

            // 外注待ち日数表示（OutsourceLeadDaysが設定されている工程のみ、区切り線付きの別セル風に表示）
            var gapBorderFactory = new FrameworkElementFactory(typeof(Border));
            gapBorderFactory.SetValue(Border.BorderBrushProperty, Brushes.LightGray);
            gapBorderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1, 0, 0, 0));
            gapBorderFactory.SetValue(Border.PaddingProperty, new Thickness(2, 0, 0, 0));
            gapBorderFactory.SetValue(Grid.ColumnProperty, 1);
            var gapVisibilityBinding = new MultiBinding { Converter = new ProcessIndexToOutsourceLeadDaysVisibilityConverter() };
            gapVisibilityBinding.Bindings.Add(new Binding("Processes"));
            gapVisibilityBinding.Bindings.Add(new Binding() { Source = index });
            gapBorderFactory.SetBinding(Border.VisibilityProperty, gapVisibilityBinding);

            var gapFactory = new FrameworkElementFactory(typeof(TextBlock));
            var gapTextBinding = new MultiBinding { Converter = new ProcessIndexToOutsourceLeadDaysConverter() };
            gapTextBinding.Bindings.Add(new Binding("Processes"));
            gapTextBinding.Bindings.Add(new Binding() { Source = index });
            gapFactory.SetBinding(TextBlock.TextProperty, gapTextBinding);
            gapFactory.SetValue(TextBlock.FontSizeProperty, 9.0);
            gapFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
            gapFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            gapFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            gapBorderFactory.AppendChild(gapFactory);

            gridFactory.AppendChild(stackFactory);
            gridFactory.AppendChild(gapBorderFactory);
            factory.AppendChild(gridFactory);
            template.VisualTree = factory;
            column.CellTemplate = template;
            OrderGrid.Columns.Add(column);
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e) {
        var window = new SettingsWindow(_viewModel);
        window.Owner = this;
        window.ShowDialog();
    }

    private async void BtnProcess_Click(object sender, RoutedEventArgs e) {
        var window = new ProcessSettingWindow();
        window.Owner = this;
        window.ShowDialog();
        await _viewModel.LoadOrdersAsync();
    }

    private void BtnHoliday_Click(object sender, RoutedEventArgs e) {
        var window = new HolidaySettingWindow();
        window.Owner = this;
        window.ShowDialog();
    }

    private async void BtnDeptSetting_Click(object sender, RoutedEventArgs e) {
        var window = new DepartmentSettingWindow();
        window.Owner = this;
        window.ShowDialog();
        // 部署マスタが変更された可能性があるため、フィルターボタンリストを更新
        await _viewModel.RefreshDepartmentFiltersAsync();
    }

    private void BtnClearFilter_Click(object sender, RoutedEventArgs e) {
        _viewModel.ClearFilter();
    }

    private void OrderRow_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) {
        // 将来：詳細表示ウィンドウ
    }
}

/// <summary>ProcessStatusを色ブラシに変換するコンバーター</summary>
public class StatusToColorConverter : System.Windows.Data.IValueConverter {
    public static Brush StatusToBrush(ProcessStatus status) => status switch {
        ProcessStatus.Completed => new SolidColorBrush(Color.FromRgb(102, 187, 106)),
        ProcessStatus.InProgress => new SolidColorBrush(Color.FromRgb(255, 224, 102)),
        ProcessStatus.Warning => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
        ProcessStatus.Overdue => new SolidColorBrush(Color.FromRgb(239, 83, 80)),
        ProcessStatus.NotStarted => new SolidColorBrush(Color.FromRgb(240, 240, 240)),
        _ => Brushes.Transparent
    };

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
        if (value is not ProcessStatus status) return Brushes.Transparent;
        return StatusToBrush(status);
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>インデックスでProcessリストを検索して背景色を返すコンバーター</summary>
public class ProcessIndexToStatusColorConverter : System.Windows.Data.IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
        if (values[0] is not IEnumerable<OrderProcess> processes) return Brushes.Transparent;
        if (values[1] is not int index) return Brushes.Transparent;
        var process = processes.ElementAtOrDefault(index);
        if (process is null) return Brushes.Transparent;
        return StatusToColorConverter.StatusToBrush(process.Status);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>インデックスでProcessリストを検索して工程名を返すコンバーター</summary>
public class ProcessIndexToNameConverter : System.Windows.Data.IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
        if (values[0] is not IEnumerable<OrderProcess> processes) return string.Empty;
        if (values[1] is not int index) return string.Empty;
        return processes.ElementAtOrDefault(index)?.ProcessName ?? string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>インデックスでProcessリストを検索して期限日テキストを返すコンバーター</summary>
public class ProcessIndexToDueDateConverter : System.Windows.Data.IMultiValueConverter {
    private readonly bool _showDueDateForNotStarted;

    public ProcessIndexToDueDateConverter(bool showDueDateForNotStarted) {
        _showDueDateForNotStarted = showDueDateForNotStarted;
    }

    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
        if (values[0] is not IEnumerable<OrderProcess> processes) return string.Empty;
        if (values[1] is not int index) return string.Empty;
        var process = processes.ElementAtOrDefault(index);
        if (process == null) return string.Empty;
        // 完了工程は受入日を表示（受入日不明は空白）
        if (process.Status == ProcessStatus.Completed)
            return process.ActualDate.HasValue ? $"✓{process.ActualDate.Value:MM/dd}" : string.Empty;

        // 未完了工程は 着手必須日/完了必須日（設定により切り替え） + 必要時間（時間単位、小数1桁）
        // 完了必須日は「この日までに完了」=矢印を日付の前に、着手必須日は「この日から着手」=矢印を日付の後に付与
        var hours = process.RequiredMinutes / 60.0;
        if (_showDueDateForNotStarted)
            return $"→{process.DueDate:MM/dd}\n({hours:F1}h)";
        return $"{process.StartDate:MM/dd}→\n({hours:F1}h)";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>インデックスでProcessリストを検索して外注待ち日数テキストを返すコンバーター</summary>
public class ProcessIndexToOutsourceLeadDaysConverter : System.Windows.Data.IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
        if (values[0] is not IEnumerable<OrderProcess> processes) return string.Empty;
        if (values[1] is not int index) return string.Empty;
        var process = processes.ElementAtOrDefault(index);
        if (process == null || process.OutsourceLeadDays <= 0) return string.Empty;
        return $"⏳\n{process.OutsourceLeadDays}日";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>インデックスでProcessリストを検索し、外注待ち日数表示の表示/非表示を返すコンバーター</summary>
public class ProcessIndexToOutsourceLeadDaysVisibilityConverter : System.Windows.Data.IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
        if (values[0] is not IEnumerable<OrderProcess> processes) return Visibility.Collapsed;
        if (values[1] is not int index) return Visibility.Collapsed;
        var process = processes.ElementAtOrDefault(index);
        return process != null && process.OutsourceLeadDays > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}
