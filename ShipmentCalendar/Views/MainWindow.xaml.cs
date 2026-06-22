using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using ShipmentCalendar.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class MainWindow : Window {
    // 外注待ち表示の背景色（App.xamlのリソースで一元管理、ProcessBarControlの外注待ちバーと同じ色）
    private static Brush OutsourceLeadBrush => (Brush)Application.Current.Resources["OutsourceLeadBrush"];

    private readonly MainViewModel _viewModel;
    // プレビュー中の行高さ（null=プレビューなし、0=自動）
    private double? _previewManualRowHeight;

    public MainWindow() {
        InitializeComponent();
        _viewModel = new MainViewModel(new SqliteHolidayRepository());
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.LoadOrdersAsync();
        _viewModel.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(_viewModel.Orders))
                BuildProcessColumns();
        };
        UpdateDueDateDisplayButtonText();
        UpdateSortModeButtonText();
        InitializeColumnVisibility();
        ApplyFixedColumnFontSize();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    // フルスクリーン切り替え前のウィンドウ状態（復元用）
    private WindowState _previousWindowState;
    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;
    private bool _isFullScreen;

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.F11) {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    private void BtnToggleFullScreen_Click(object sender, RoutedEventArgs e) {
        ToggleFullScreen();
    }

    /// <summary>F11キー・ツールバーボタンでフルスクリーン表示（タイトルバー・タスクバーを隠す）と通常表示を切り替える</summary>
    private void ToggleFullScreen() {
        if (!_isFullScreen) {
            _previousWindowState = WindowState;
            _previousWindowStyle = WindowStyle;
            _previousResizeMode = ResizeMode;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
        else {
            WindowStyle = _previousWindowStyle;
            ResizeMode = _previousResizeMode;
            WindowState = _previousWindowState;
        }
        _isFullScreen = !_isFullScreen;
        BtnToggleFullScreen.Content = _isFullScreen ? "ウィンドウ表示" : "フルスクリーン";
    }

    // 列表示設定MenuItemと対応するDataGridColumn・設定プロパティの組み合わせ（初回アクセス時に生成してキャッシュする）
    private (System.Windows.Controls.MenuItem MenuItem, DataGridColumn Column, Func<AppSettings, bool> Getter, Action<AppSettings, bool> Setter)[]? _columnVisibilityMappings;
    private (System.Windows.Controls.MenuItem MenuItem, DataGridColumn Column, Func<AppSettings, bool> Getter, Action<AppSettings, bool> Setter)[] ColumnVisibilityMappings => _columnVisibilityMappings ??= [
        (MnuColDeliveryDate,      ColDeliveryDate,      s => s.ShowColumnDeliveryDate,      (s, v) => s.ShowColumnDeliveryDate = v),
        (MnuColCompletionDate,    ColCompletionDate,    s => s.ShowColumnCompletionDate,    (s, v) => s.ShowColumnCompletionDate = v),
        (MnuColItemNumber,        ColItemNumber,        s => s.ShowColumnItemNumber,        (s, v) => s.ShowColumnItemNumber = v),
        (MnuColModelCode,         ColModelCode,         s => s.ShowColumnModelCode,         (s, v) => s.ShowColumnModelCode = v),
        (MnuColProductName,       ColProductName,       s => s.ShowColumnProductName,       (s, v) => s.ShowColumnProductName = v),
        (MnuColManufactureNumber, ColManufactureNumber, s => s.ShowColumnManufactureNumber, (s, v) => s.ShowColumnManufactureNumber = v),
        (MnuColPlannedQuantity,   ColPlannedQuantity,   s => s.ShowColumnPlannedQuantity,   (s, v) => s.ShowColumnPlannedQuantity = v),
    ];

    // MenuItemのChecked/Uncheckedイベントを設定値の反映として処理するか（初期化中はfalseにして保存を抑制する）
    private bool _columnVisibilityEventsEnabled;

    /// <summary>保存済みの設定値をMenuItemとDataGridColumnの表示状態に反映する（保存はしない）</summary>
    private void InitializeColumnVisibility() {
        _columnVisibilityEventsEnabled = false;
        foreach (var (menuItem, column, getter, _) in ColumnVisibilityMappings) {
            var isVisible = getter(_viewModel.Settings);
            menuItem.IsChecked = isVisible;
            column.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateProcessModeButtonText();
        _columnVisibilityEventsEnabled = true;
    }

    private void BtnToggleProcessMode_Click(object sender, RoutedEventArgs e) {
        var settings = _viewModel.Settings;
        // バー→セル→バー の順でトグル
        if (settings.ShowProcessBar && !settings.ShowProcessColumns) {
            settings.ShowProcessBar = false;
            settings.ShowProcessColumns = true;
        } else {
            settings.ShowProcessBar = true;
            settings.ShowProcessColumns = false;
        }
        _viewModel.SaveSettings();
        UpdateProcessModeButtonText();
        _lastColumnSignature = null;
        BuildProcessColumns();
    }

    /// <summary>工程表示モードボタンの文言を現在の設定に合わせて更新する</summary>
    private void UpdateProcessModeButtonText() {
        var settings = _viewModel.Settings;
        BtnToggleProcessMode.Content = (settings.ShowProcessBar, settings.ShowProcessColumns) switch {
            (true, false) => "工程: バー",
            (false, true) => "工程: リスト",
            (true, true)  => "工程: バー＋リスト",
            _             => "工程: なし",
        };
    }

    private void ColumnVisibilityCheckBox_Changed(object sender, RoutedEventArgs e) {
        if (!_columnVisibilityEventsEnabled) return;

        var menuItem = (MenuItem)sender;
        var mapping = ColumnVisibilityMappings.FirstOrDefault(m => m.MenuItem == menuItem);
        if (mapping.MenuItem == null) return;

        var isChecked = menuItem.IsChecked;
        mapping.Column.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
        mapping.Setter(_viewModel.Settings, isChecked);
        _viewModel.SaveSettings();
    }

    /// <summary>表示日切り替えボタンの文言を現在の設定に合わせて更新する</summary>
    private void UpdateDueDateDisplayButtonText() {
        BtnToggleDueDateDisplay.Content = _viewModel.Settings.ShowDueDateForNotStarted
            ? "表示中：完了期限"
            : "表示中：着手期限";
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

    /// <summary>フォントサイズ設定を設定値から適用し、行の高さを再計算する</summary>
    private void ApplyFixedColumnFontSize() {
        var settings = _viewModel.Settings;
        OrderGrid.FontSize = settings.FixedColumnFontSize;
        UpdateRowHeight();
    }

    /// <summary>工程列の表示行数（工程名＋期限日/標準時間の設定状況）と各列のフォントサイズから行の高さを計算する</summary>
    private void UpdateRowHeight() {
        var settings = _viewModel.Settings;
        // プレビュー中はダイアログの値を優先（0=自動、正値=手動、null=プレビューなし）
        var effectiveManual = _previewManualRowHeight ?? settings.ManualRowHeight;
        OrderGrid.RowHeight = effectiveManual > 0 ? effectiveManual : CalculateAutoRowHeight();
    }

    // 直前に構築した工程列の構成（変化がなければ再構築をスキップする）
    private (int MaxProcessCount, bool ShowDueDateForNotStarted, bool ShowProcessDate, bool ShowProcessRequiredHours, bool ShowRequiredTimeInMinutes, double ProcessColumnFontSize, double ProcessBarFontSize, bool ShowProcessBar, bool ShowProcessColumns)? _lastColumnSignature;

    /// <summary>工程列をインデックスベースで動的生成する（列ヘッダー: 1, 2, 3...）</summary>
    private void BuildProcessColumns() {
        UpdateRowHeight();

        // 全注文中の最大工程数を列数とする
        var maxProcessCount = _viewModel.Orders.Count > 0 ? _viewModel.Orders.Max(o => o.Processes.Count) : 0;
        var settings = _viewModel.Settings;
        var signature = (maxProcessCount, settings.ShowDueDateForNotStarted, settings.ShowProcessDate, settings.ShowProcessRequiredHours, settings.ShowRequiredTimeInMinutes, settings.ProcessColumnFontSize, settings.ProcessBarFontSize, settings.ShowProcessBar, settings.ShowProcessColumns);
        if (signature == _lastColumnSignature) return;
        _lastColumnSignature = signature;

        // 固定列（出荷日・完了日・品目番号・機種コード・品目名・製番・計画数）以外を削除
        while (OrderGrid.Columns.Count > 7)
            OrderGrid.Columns.RemoveAt(7);

        if (maxProcessCount == 0) return;

        // 工程バー列（全工程を1本のバーで表示）
        if (settings.ShowProcessBar) {
            var barColumn = new DataGridTemplateColumn {
                Header = "工程バー",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            };
            var barTemplate = new DataTemplate();
            var barFactory = new FrameworkElementFactory(typeof(ProcessBarControl));
            barFactory.SetBinding(ProcessBarControl.ProcessesProperty, new Binding("Processes"));
            barFactory.SetValue(ProcessBarControl.BarFontSizeProperty, settings.ProcessBarFontSize);
            barFactory.SetValue(ProcessBarControl.ShowRequiredTimeInMinutesProperty, settings.ShowRequiredTimeInMinutes);
            barTemplate.VisualTree = barFactory;
            barColumn.CellTemplate = barTemplate;
            // 工程バー列はフォーカス枠を出さず、選択状態になっても背景の青いハイライトを表示しない
            var barCellStyle = new Style(typeof(DataGridCell));
            barCellStyle.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            barCellStyle.Triggers.Add(new Trigger {
                Property = DataGridCell.IsSelectedProperty,
                Value = true,
                Setters = { new Setter(Control.BackgroundProperty, Brushes.Transparent) },
            });
            barColumn.CellStyle = barCellStyle;
            OrderGrid.Columns.Add(barColumn);
        }

        if (!settings.ShowProcessColumns) return;

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
            nameFactory.SetValue(TextBlock.FontSizeProperty, _viewModel.Settings.ProcessColumnFontSize);
            nameFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            nameFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);

            stackFactory.AppendChild(nameFactory);

            // 期限日・標準時間テキスト（両方OFFの場合は生成しない、1行にまとめて表示）
            if (_viewModel.Settings.ShowProcessDate || _viewModel.Settings.ShowProcessRequiredHours) {
                var dateHoursFactory = new FrameworkElementFactory(typeof(TextBlock));
                var dateHoursBinding = new MultiBinding {
                    Converter = new ProcessIndexToDateAndHoursConverter(
                        _viewModel.Settings.ShowDueDateForNotStarted,
                        _viewModel.Settings.ShowProcessDate,
                        _viewModel.Settings.ShowProcessRequiredHours,
                        _viewModel.Settings.ShowRequiredTimeInMinutes)
                };
                dateHoursBinding.Bindings.Add(new Binding("Processes"));
                dateHoursBinding.Bindings.Add(new Binding() { Source = index });
                dateHoursFactory.SetBinding(TextBlock.TextProperty, dateHoursBinding);
                dateHoursFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                dateHoursFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
                dateHoursFactory.SetValue(TextBlock.FontSizeProperty, _viewModel.Settings.ProcessColumnFontSize);
                dateHoursFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
                stackFactory.AppendChild(dateHoursFactory);
            }

            // 外注待ち日数表示（OutsourceLeadDaysが設定されている工程のみ、区切り線付きの別セル風に表示）
            var gapBorderFactory = new FrameworkElementFactory(typeof(Border));
            gapBorderFactory.SetValue(Border.BackgroundProperty, OutsourceLeadBrush);
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
        var window = new SettingsWindow(_viewModel) { Owner = this };
        window.ShowDialog();
    }

    private async void BtnProcess_Click(object sender, RoutedEventArgs e) {
        var window = new ProcessSettingWindow() { Owner = this };
        window.ShowDialog();
        await _viewModel.LoadOrdersAsync();
    }

    private void BtnHoliday_Click(object sender, RoutedEventArgs e) {
        var window = new HolidaySettingWindow() { Owner = this };
        window.ShowDialog();
    }

    private async void BtnDeptSetting_Click(object sender, RoutedEventArgs e) {
        var window = new DepartmentSettingWindow() { Owner = this };
        window.ShowDialog();
        // 部署マスタが変更された可能性があるため、フィルターボタンリストを更新
        await _viewModel.RefreshDepartmentFiltersAsync();
    }

    /// <summary>表示設定ダイアログからのリアルタイムプレビュー用（設定には保存しない）</summary>
    public void PreviewRowHeight(double height) {
        _previewManualRowHeight = height;
        OrderGrid.RowHeight = height > 0 ? height : CalculateAutoRowHeight();
    }

    /// <summary>フォントサイズのリアルタイムプレビュー用（設定には保存しない）</summary>
    public void PreviewFontSizes(double fixedSize, double processBarSize, double processColumnSize = 0) {
        if (fixedSize > 0) {
            _viewModel.Settings.FixedColumnFontSize = fixedSize;
            OrderGrid.FontSize = fixedSize;
            UpdateRowHeight();
        }
        var needRebuild = false;
        if (processBarSize > 0) {
            _viewModel.Settings.ProcessBarFontSize = processBarSize;
            needRebuild = true;
        }
        if (processColumnSize > 0) {
            _viewModel.Settings.ProcessColumnFontSize = processColumnSize;
            needRebuild = true;
        }
        if (needRebuild) {
            _lastColumnSignature = null; // キャッシュを無効化して再構築を強制
            BuildProcessColumns();
            UpdateRowHeight();
        }
    }

    private double CalculateAutoRowHeight() {
        var settings = _viewModel.Settings;
        double processHeight = 0;
        if (settings.ShowProcessColumns) {
            var processLineCount = 1 + (settings.ShowProcessDate || settings.ShowProcessRequiredHours ? 1 : 0);
            processHeight = processLineCount * (settings.ProcessColumnFontSize * 1.8) + 10;
        }
        if (settings.ShowProcessBar)
            processHeight = Math.Max(processHeight, ProcessBarControl.DateBarHeight + (settings.ProcessBarFontSize * 1.8) + 8);
        return Math.Max(processHeight, settings.FixedColumnFontSize * 1.8 + 8);
    }

    private void BtnDisplaySettings_Click(object sender, RoutedEventArgs e) {
        var window = new DisplaySettingsWindow(_viewModel, this) { Owner = this };
        window.ShowDialog();
        _previewManualRowHeight = null;
        if (window.DialogResult == true) {
            ApplyFixedColumnFontSize();
            BuildProcessColumns();
        }
    }

    private void BtnClearFilter_Click(object sender, RoutedEventArgs e) {
        _viewModel.ClearFilter();
    }

    private void OrderRow_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) {
        if (sender is not DataGridRow row || row.Item is not Order order) return;
        new OrderDetailWindow(order, _viewModel.Settings.ShowRequiredTimeInMinutes) { Owner = this }.ShowDialog();
    }
}

/// <summary>ProcessStatusを色ブラシに変換するコンバーター</summary>
public class StatusToColorConverter : System.Windows.Data.IValueConverter {
    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public static Brush StatusToBrush(ProcessStatus status) => status switch {
        ProcessStatus.Completed  => Res("StatusCompleted"),
        ProcessStatus.InProgress => Res("StatusInProgress"),
        ProcessStatus.Warning    => Res("StatusWarning"),
        ProcessStatus.Overdue    => Res("StatusOverdue"),
        ProcessStatus.NotStarted => Res("StatusNotStarted"),
        _ => Brushes.Transparent
    };

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
        if (value is not ProcessStatus status) return Brushes.Transparent;
        return StatusToBrush(status);
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>bool値を反転するコンバーター（"本日のみ"チェック時にDatePickerを無効化するために使用）</summary>
public class InverseBooleanConverter : System.Windows.Data.IValueConverter {
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => !(value is bool b && b);

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => !(value is bool b && b);
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

/// <summary>インデックスでProcessリストを検索して期限日・標準時間（必要時間）テキストを1行にまとめて返すコンバーター</summary>
public class ProcessIndexToDateAndHoursConverter(bool showDueDateForNotStarted, bool showDate, bool showHours, bool showRequiredTimeInMinutes) : System.Windows.Data.IMultiValueConverter {

    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
        if (values[0] is not IEnumerable<OrderProcess> processes) return string.Empty;
        if (values[1] is not int index) return string.Empty;
        var process = processes.ElementAtOrDefault(index);
        if (process == null) return string.Empty;

        var dateText = showDate ? GetDateText(process) : string.Empty;
        var hoursText = showHours ? GetHoursText(process) : string.Empty;

        return string.Join(" ", new[] { dateText, hoursText }.Where(s => !string.IsNullOrEmpty(s)));
    }

    /// <summary>完了工程は受入日（✓付き）、未完了工程は着手必須日/完了必須日（設定により切り替え）を返す</summary>
    private string GetDateText(OrderProcess process) {
        // 完了工程は受入日を表示（受入日不明は空白）
        if (process.Status == ProcessStatus.Completed)
            return process.ActualDate.HasValue ? $"✓{process.ActualDate.Value:MM/dd}" : string.Empty;

        // 未完了工程は 着手必須日/完了必須日（設定により切り替え）を表示
        // 完了必須日は「この日までに完了」=矢印を日付の前に、着手必須日は「この日から着手」=矢印を日付の後に付与
        if (showDueDateForNotStarted)
            return $"→{process.DueDate:MM/dd}";
        return $"{process.StartDate:MM/dd}→";
    }

    /// <summary>完了工程は標準時間を表示しない</summary>
    private string GetHoursText(OrderProcess process) {
        if (process.Status == ProcessStatus.Completed) return string.Empty;

        return $"({process.GetRequiredTimeDescription(showRequiredTimeInMinutes)})";
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
