using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class DepartmentLoadWindow : Window {
    private readonly AppSettings _settings;
    private IEnumerable<Order> _orders = [];
    private IEnumerable<Department> _departments = [];

    public DepartmentLoadWindow(IEnumerable<Order> orders, AppSettings settings) {
        InitializeComponent();
        _settings = settings;
        TxtCautionMinutes.Text = settings.CongestionCautionMinutes.ToString(CultureInfo.InvariantCulture);
        TxtConcentratedMinutes.Text = settings.CongestionConcentratedMinutes.ToString(CultureInfo.InvariantCulture);
        Loaded += async (_, _) => await LoadAsync(orders);
    }

    private async Task LoadAsync(IEnumerable<Order> orders) {
        _orders = orders;
        _departments = await SqliteDepartmentRepository.GetAllAsync();
        RebuildGrid();
    }

    private void BtnApplyThreshold_Click(object sender, RoutedEventArgs e) {
        if (!double.TryParse(TxtCautionMinutes.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var caution)
            || !double.TryParse(TxtConcentratedMinutes.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var concentrated)
            || !double.IsFinite(caution) || !double.IsFinite(concentrated)
            || caution < 0 || concentrated <= caution) {
            MessageBox.Show("集中の分数は、やや集中の分数より大きい0以上の数値で入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.CongestionCautionMinutes = caution;
        _settings.CongestionConcentratedMinutes = concentrated;
        AppSettingsService.Save(_settings);
        RebuildGrid();
    }

    private void RebuildGrid() {
        var rows = DepartmentLoadCalculator.Aggregate(_orders, _departments, _settings.CongestionCautionMinutes, _settings.CongestionConcentratedMinutes);

        if (rows.Count == 0 || rows[0].Cells.Count == 0) {
            TxtEmpty.Visibility = Visibility.Visible;
            LoadGrid.ItemsSource = null;
            return;
        }

        TxtEmpty.Visibility = Visibility.Collapsed;
        if (LoadGrid.Columns.Count <= 1) {
            for (int i = 0; i < rows[0].Cells.Count; i++)
                LoadGrid.Columns.Add(BuildDateColumn(rows[0].Cells[i].Date, i));
        }

        LoadGrid.ItemsSource = rows;
    }

    private static DataGridTemplateColumn BuildDateColumn(DateOnly date, int index) {
        var column = new DataGridTemplateColumn { Header = date.ToString("M/d"), Width = 64 };

        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)));
        borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 0));
        borderFactory.SetBinding(Border.BackgroundProperty, new Binding($"Cells[{index}].Level") { Converter = new CongestionLevelToBrushConverter() });
        borderFactory.SetBinding(FrameworkElement.ToolTipProperty, new Binding($"Cells[{index}].Tooltip"));

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        textFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        textFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
        // 行選択時にDataGridの既定スタイルで文字色が白に切り替わり、白背景（通常）セルで見えなくなるため固定色にする
        textFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
        textFactory.SetBinding(TextBlock.TextProperty, new Binding($"Cells[{index}].DisplayText"));
        borderFactory.AppendChild(textFactory);

        var template = new DataTemplate { VisualTree = borderFactory };
        column.CellTemplate = template;
        return column;
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
