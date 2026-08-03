using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class DepartmentLoadWindow : Window {
    // 日付セルの文字サイズ・最大行数（件数/時間/充足率/欠員の4行）。RowHeightをこの値から算出することで、
    // フォントサイズを変更してもDataGridの行高さが自動的に追従し、部署によって行高さがばらつくのを防ぐ
    private const double CellFontSize = 10.0;
    private const int MaxCellLines = 4;

    private readonly AppSettings _settings;
    private IEnumerable<Order> _orders = [];
    private IEnumerable<Department> _departments = [];
    private IEnumerable<DepartmentAbsence> _absences = [];

    public DepartmentLoadWindow(IEnumerable<Order> orders, AppSettings settings) {
        InitializeComponent();
        _settings = settings;
        TxtCautionPercent.Text = settings.CongestionCautionPercent.ToString(CultureInfo.InvariantCulture);
        TxtConcentratedPercent.Text = settings.CongestionConcentratedPercent.ToString(CultureInfo.InvariantCulture);
        // 行の上下余白（DataGridCellの既定Padding相当）を含め、4行分の高さを確保する
        LoadGrid.RowHeight = CellFontSize * 1.3 * MaxCellLines + 8;
        Loaded += async (_, _) => await LoadAsync(orders);
    }

    private async Task LoadAsync(IEnumerable<Order> orders) {
        _orders = orders;
        var departmentsTask = SqliteDepartmentRepository.GetAllAsync();
        var absencesTask = SqliteDepartmentAbsenceRepository.GetAllAsync();
        await Task.WhenAll(departmentsTask, absencesTask);
        _departments = await departmentsTask;
        _absences = await absencesTask;
        RebuildGrid();
    }

    private void BtnApplyThreshold_Click(object sender, RoutedEventArgs e) {
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
        RebuildGrid();
    }

    private void RebuildGrid() {
        var rows = DepartmentLoadCalculator.Aggregate(_orders, _departments, _absences, _settings.DayMinutes, _settings.CongestionCautionPercent, _settings.CongestionConcentratedPercent);

        if (rows.Count == 0 || rows[0].Cells.Count == 0) {
            TxtEmpty.Visibility = Visibility.Visible;
            TxtHeadcountWarning.Visibility = Visibility.Collapsed;
            LoadGrid.ItemsSource = null;
            return;
        }

        TxtEmpty.Visibility = Visibility.Collapsed;
        TxtHeadcountWarning.Visibility = rows.Any(r => r.DepartmentId != 0 && r.Headcount <= 0) ? Visibility.Visible : Visibility.Collapsed;
        if (LoadGrid.Columns.Count <= 1) {
            for (int i = 0; i < rows[0].Cells.Count; i++)
                LoadGrid.Columns.Add(BuildDateColumn(rows[0].Cells[i].Date, i));
        }

        LoadGrid.ItemsSource = rows;
    }

    private DataGridTemplateColumn BuildDateColumn(DateOnly date, int index) {
        var isToday = date == DateOnly.FromDateTime(DateTime.Today);
        var column = new DataGridTemplateColumn { Header = date.ToString("M/d"), Width = 64 };

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
        borderFactory.SetBinding(Border.BackgroundProperty, new Binding($"Cells[{index}].Level") { Converter = new CongestionLevelToBrushConverter() });
        borderFactory.SetBinding(FrameworkElement.ToolTipProperty, new Binding($"Cells[{index}].Tooltip"));

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        textFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        textFactory.SetValue(TextBlock.FontSizeProperty, CellFontSize);
        // 行選択時にDataGridの既定スタイルで文字色が白に切り替わり、白背景（通常）セルで見えなくなるため固定色にする
        textFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
        textFactory.SetBinding(TextBlock.TextProperty, new Binding($"Cells[{index}].DisplayText"));
        borderFactory.AppendChild(textFactory);

        // クリックで、このセルの集計元になった注文一覧をサイドパネルに表示する
        borderFactory.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler((sender, e) => {
            if (sender is not FrameworkElement { DataContext: DepartmentLoadRow row } target) return;
            ShowCellDetail(row.Cells[index]);
        }));

        var template = new DataTemplate { VisualTree = borderFactory };
        column.CellTemplate = template;
        return column;
    }

    private void ShowCellDetail(DepartmentLoadCell cell) {
        if (cell.Items.Count == 0) {
            TxtDetailTitle.Text = "日付セルを選択すると明細を表示します";
            CellDetailList.ItemsSource = null;
            return;
        }

        TxtDetailTitle.Text = $"{cell.Date:M/d}　{cell.ProcessCount}件";
        CellDetailList.ItemsSource = cell.Items;
    }

    private void CellDetailList_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        if (CellDetailList.SelectedItem is not DepartmentLoadCellItem item) return;
        new OrderDetailWindow(item.Order, _settings.ShowRequiredTimeInMinutes, _settings.DayMinutes) { Owner = this }.ShowDialog();
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
