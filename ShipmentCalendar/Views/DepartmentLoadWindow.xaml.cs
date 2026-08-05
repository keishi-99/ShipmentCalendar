using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using ShipmentCalendar.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class DepartmentLoadWindow : Window {
    // 日付セルの文字サイズ・最大行数（1行目：割合|超過時間、2行目：件数|時間|欠員）。RowHeightをこの値から算出することで、
    // フォントサイズを変更してもDataGridの行高さが自動的に追従し、部署によって行高さがばらつくのを防ぐ
    private const double CellFontSize = 10.0;
    private const int MaxCellLines = 2;
    // 静的列（部署・超過）の数。日付列はこれより後ろに動的追加される
    private const int StaticColumnCount = 2;
    // 超過列のColumns内インデックス（0=部署、1=超過）
    private const int OverdueColumnIndex = 1;

    private readonly AppSettings _settings;
    private readonly ProductCategoryClassifier _categoryClassifier;
    private readonly SqliteHolidayRepository _holidayRepository = new();
    private IEnumerable<Order> _allOrders = [];
    private IEnumerable<Department> _departments = [];
    private IEnumerable<DepartmentAbsence> _absences = [];
    private HashSet<DateOnly> _holidayDates = [];
    // 初回ロードと「適用」ボタンの初回クリックが重なった場合に、古い呼び出しが後から完了して
    // 列を重複追加しないよう、呼び出しごとに世代番号を発行して判定する
    private int _rebuildRevision;

    public DepartmentLoadWindow(IEnumerable<Order> orders, ProductCategoryClassifier categoryClassifier, AppSettings settings) {
        InitializeComponent();
        _settings = settings;
        _categoryClassifier = categoryClassifier;
        TxtCautionPercent.Text = settings.CongestionCautionPercent.ToString(CultureInfo.InvariantCulture);
        TxtConcentratedPercent.Text = settings.CongestionConcentratedPercent.ToString(CultureInfo.InvariantCulture);
        // 行の上下余白（DataGridCellの既定Padding相当）を含め、MaxCellLines行分の高さを確保する
        LoadGrid.RowHeight = CellFontSize * 1.3 * MaxCellLines + 8;
        CmbCategory.ItemsSource = MainViewModel.ProductCategoryOptions;
        CmbCategory.SelectedIndex = 0;
        Loaded += async (_, _) => await LoadAsync(orders);
    }

    private async Task LoadAsync(IEnumerable<Order> orders) {
        _allOrders = orders;
        var departmentsTask = SqliteDepartmentRepository.GetAllAsync();
        var absencesTask = SqliteDepartmentAbsenceRepository.GetAllAsync();
        await Task.WhenAll(departmentsTask, absencesTask);
        _departments = await departmentsTask;
        _absences = await absencesTask;
        await RebuildGridAsync();
    }

    // 区分の切り替えで対象注文の日付範囲が変わりうるため、日付列だけを作り直す（先頭の「部署」「超過」列はXAML定義の静的列なので残す）
    private async void CmbCategory_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        // Window.Loaded前（コンストラクタでの初期選択設定）は_allOrders等が未ロードのため何もしない
        if (!IsLoaded) return;

        while (LoadGrid.Columns.Count > StaticColumnCount)
            LoadGrid.Columns.RemoveAt(LoadGrid.Columns.Count - 1);
        // 区分が変わると明細パネルが示していたセルの内容と現在の集計が対応しなくなるため表示をリセットする
        TxtDetailTitle.Text = "日付セルを選択すると明細を表示します";
        CellDetailGrid.ItemsSource = null;
        await RebuildGridAsync();
    }

    private IEnumerable<Order> FilteredOrders() => (CmbCategory.SelectedItem as string) switch {
        "製品" => _allOrders.Where(o => _categoryClassifier.Classify(o) == ProductCategoryClassifier.Product),
        "半製品" => _allOrders.Where(o => _categoryClassifier.Classify(o) == ProductCategoryClassifier.SemiProduct),
        "半製品（工程未登録）" => _allOrders.Where(o => _categoryClassifier.IsUnregisteredSemiProduct(o)),
        "どちらでもない" => _allOrders.Where(o => _categoryClassifier.Classify(o) == ProductCategoryClassifier.Other),
        _ => _allOrders,
    };

    private async void BtnApplyThreshold_Click(object sender, RoutedEventArgs e) {
        if (!double.TryParse(TxtCautionPercent.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var caution)
            || !double.TryParse(TxtConcentratedPercent.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var concentrated)
            || !double.IsFinite(caution) || !double.IsFinite(concentrated)
            || caution < 0 || concentrated <= caution) {
            MessageBox.Show("集中の充足率は、やや集中の充足率より大きい0以上の数値で入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.CongestionCautionPercent = caution;
        _settings.CongestionConcentratedPercent = concentrated;
        AppSettingsService.Save(_settings);
        await RebuildGridAsync();
    }

    private async Task RebuildGridAsync() {
        var revision = ++_rebuildRevision;

        var rows = DepartmentLoadCalculator.Aggregate(FilteredOrders(), _departments, _absences, _settings.DayMinutes, _settings.CongestionCautionPercent, _settings.CongestionConcentratedPercent, DateOnly.FromDateTime(DateTime.Today));

        // 対象工程が1件もない場合のみ「空」。超過工程のみで日付セルが0件の場合は「超過」列だけで表示するため空扱いにしない
        if (rows.Count == 0) {
            TxtEmpty.Visibility = Visibility.Visible;
            TxtHeadcountWarning.Visibility = Visibility.Collapsed;
            LoadGrid.ItemsSource = null;
            return;
        }

        TxtEmpty.Visibility = Visibility.Collapsed;
        TxtHeadcountWarning.Visibility = rows.Any(r => r.DepartmentId != 0 && r.Headcount <= 0) ? Visibility.Visible : Visibility.Collapsed;
        if (LoadGrid.Columns.Count <= StaticColumnCount) {
            // カレンダー表示期間（複数年にまたがる可能性がある）をカバーする休日を取得する
            var years = rows[0].Cells.Select(c => c.Date.Year).Distinct();
            var holidayLists = await Task.WhenAll(years.Select(y => _holidayRepository.GetByYearAsync(y)));

            // より新しい呼び出しが既に列を構築済みの場合、この呼び出しでは追加しない（重複追加防止）
            if (revision != _rebuildRevision) return;

            _holidayDates = holidayLists.SelectMany(h => h).Select(h => h.Date).ToHashSet();

            for (int i = 0; i < rows[0].Cells.Count; i++)
                LoadGrid.Columns.Add(BuildDateColumn(rows[0].Cells[i].Date, i, _holidayDates.Contains(rows[0].Cells[i].Date)));
        }

        LoadGrid.ItemsSource = rows;
    }

    private static DataGridTemplateColumn BuildDateColumn(DateOnly date, int index, bool isHoliday) {
        var isToday = date == DateOnly.FromDateTime(DateTime.Today);
        var column = new DataGridTemplateColumn { Header = date.ToString("M/d"), Width = 100 };

        if (isToday) {
            // BasedOnを指定しないとテーマの既定スタイル(Template)を引き継げず、ヘッダーが空白になるため明示的に継承する
            var baseHeaderStyle = (Style)Application.Current.FindResource(typeof(DataGridColumnHeader));
            var headerStyle = new Style(typeof(DataGridColumnHeader), baseHeaderStyle);
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, (Brush)Application.Current.Resources["AccentColor"]));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
            column.HeaderStyle = headerStyle;
        }

        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.BorderBrushProperty, isToday
            ? (Brush)Application.Current.Resources["AccentColor"]
            : new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)));
        borderFactory.SetValue(Border.BorderThicknessProperty, isToday ? new Thickness(2, 0, 2, 0) : new Thickness(0, 0, 1, 0));
        // 休日は集中度に関わらず稼働しないため、Levelバインディングではなく固定のグレーで塗る
        if (isHoliday)
            borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)));
        else
            borderFactory.SetBinding(Border.BackgroundProperty, new Binding($"Cells[{index}].Level") { Converter = new CongestionLevelToBrushConverter() });
        borderFactory.SetBinding(FrameworkElement.ToolTipProperty, new Binding($"Cells[{index}].Tooltip"));

        // 1行目・2行目で列数が異なる（割合|超過時間の2列 / 件数|時間|欠員の3列）ため、
        // 外側は2行1列のみとし、各行に列数の異なる内側Gridを個別に持たせる
        var gridFactory = new FrameworkElementFactory(typeof(Grid));
        gridFactory.AppendChild(new FrameworkElementFactory(typeof(RowDefinition)));
        gridFactory.AppendChild(new FrameworkElementFactory(typeof(RowDefinition)));

        // 1行目：[割合|超過時間]（1:1）
        var row1Factory = new FrameworkElementFactory(typeof(Grid));
        row1Factory.SetValue(Grid.RowProperty, 0);
        row1Factory.AppendChild(new FrameworkElementFactory(typeof(ColumnDefinition)));
        row1Factory.AppendChild(new FrameworkElementFactory(typeof(ColumnDefinition)));

        var fulfillmentFactory = BuildCellTextFactory($"Cells[{index}].FulfillmentPercentText", HorizontalAlignment.Left);
        fulfillmentFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        fulfillmentFactory.SetValue(Grid.ColumnProperty, 0);
        row1Factory.AppendChild(fulfillmentFactory);

        var overtimeFactory = BuildCellTextFactory($"Cells[{index}].OvertimeText");
        overtimeFactory.SetValue(Grid.ColumnProperty, 1);
        row1Factory.AppendChild(overtimeFactory);

        gridFactory.AppendChild(row1Factory);

        // 2行目：[件数|時間|欠員]（均等3分割）
        var row2Factory = new FrameworkElementFactory(typeof(Grid));
        row2Factory.SetValue(Grid.RowProperty, 1);
        row2Factory.AppendChild(new FrameworkElementFactory(typeof(ColumnDefinition)));
        row2Factory.AppendChild(new FrameworkElementFactory(typeof(ColumnDefinition)));
        row2Factory.AppendChild(new FrameworkElementFactory(typeof(ColumnDefinition)));

        var countFactory = BuildCellTextFactory($"Cells[{index}].ProcessCountText");
        countFactory.SetValue(Grid.ColumnProperty, 0);
        row2Factory.AppendChild(countFactory);

        var hoursFactory = BuildCellTextFactory($"Cells[{index}].TotalHoursText");
        hoursFactory.SetValue(Grid.ColumnProperty, 1);
        row2Factory.AppendChild(hoursFactory);

        var absentFactory = BuildCellTextFactory($"Cells[{index}].AbsentCellText");
        absentFactory.SetValue(Grid.ColumnProperty, 2);
        row2Factory.AppendChild(absentFactory);

        gridFactory.AppendChild(row2Factory);

        borderFactory.AppendChild(gridFactory);

        var template = new DataTemplate { VisualTree = borderFactory };
        column.CellTemplate = template;
        return column;
    }

    /// <summary>セル内の1マス分のTextBlockを構築する。幅が狭いため、収まらない場合は末尾を省略する。
    /// 既定は中央揃えだが、割合は超過時間の有無で文字数が大きく変わり中央揃えだと数値の位置がずれるため左揃えにする</summary>
    private static FrameworkElementFactory BuildCellTextFactory(string bindingPath, HorizontalAlignment horizontalAlignment = HorizontalAlignment.Center) {
        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetValue(TextBlock.HorizontalAlignmentProperty, horizontalAlignment);
        textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        textFactory.SetValue(TextBlock.TextAlignmentProperty, horizontalAlignment == HorizontalAlignment.Left ? TextAlignment.Left : TextAlignment.Center);
        if (horizontalAlignment == HorizontalAlignment.Left)
            textFactory.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 0, 0));
        textFactory.SetValue(TextBlock.FontSizeProperty, CellFontSize);
        textFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        // 行選択時にDataGridの既定スタイルで文字色が白に切り替わり、白背景（通常）セルで見えなくなるため固定色にする
        textFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
        textFactory.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
        return textFactory;
    }

    // マウスクリック・キーボードでのセル移動のどちらでも選択セルが変わるたびに発火するため、明細更新をここに一本化する
    private void LoadGrid_CurrentCellChanged(object sender, EventArgs e) {
        if (LoadGrid.CurrentCell.Item is not DepartmentLoadRow row || LoadGrid.CurrentCell.Column is not { } column) return;

        var columnIndex = LoadGrid.Columns.IndexOf(column);
        if (columnIndex == OverdueColumnIndex) ShowOverdueDetail(row);
        else if (columnIndex >= StaticColumnCount) ShowCellDetail(row.Cells[columnIndex - StaticColumnCount]);
    }

    private void ShowCellDetail(DepartmentLoadCell cell) => ShowDetail($"{cell.Date:M/d}　{cell.ProcessCount}件", cell.Items);

    private void ShowOverdueDetail(DepartmentLoadRow row) => ShowDetail($"超過　{row.OverdueProcessCount}件", row.OverdueItems);

    private void ShowDetail(string title, List<DepartmentLoadCellItem> items) {
        if (items.Count == 0) {
            TxtDetailTitle.Text = "日付セルを選択すると明細を表示します";
            CellDetailGrid.ItemsSource = null;
            return;
        }

        TxtDetailTitle.Text = title;
        CellDetailGrid.ItemsSource = items;
    }

    private void CellDetailGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        // 行のない余白部分のダブルクリックでは、選択済み行が残っていても何もしない
        if (e.OriginalSource is not DependencyObject source || !IsInRow(source)) return;
        if (CellDetailGrid.SelectedItem is not DepartmentLoadCellItem item) return;
        new OrderDetailWindow(item.Order, _settings.ShowRequiredTimeInMinutes, _settings.DayMinutes) { Owner = this }.ShowDialog();
    }

    private static bool IsInRow(DependencyObject source) {
        for (var current = source; current != null; current = VisualTreeHelper.GetParent(current)) {
            if (current is DataGridRow) return true;
        }
        return false;
    }
}

/// <summary>CongestionLevelを対応するブラシ（App.xamlのCongestionXxxリソース）に変換するコンバーター</summary>
public class CongestionLevelToBrushConverter : IValueConverter {
    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is not CongestionLevel level) return Brushes.Transparent;
        return level switch {
            CongestionLevel.Caution => Res("CongestionCaution"),
            CongestionLevel.Concentrated => Res("CongestionConcentrated"),
            _ => Res("CongestionNormal")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
