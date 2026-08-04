using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class DepartmentAbsenceSettingWindow : Window {
    private readonly SqliteHolidayRepository _holidayRepository = new();
    private List<Department> _departments = [];
    private List<DateOnly> _currentDates = [];
    private HashSet<DateOnly> _holidayDates = [];

    // 休日列は入力不可のためグレーアウトして区別する（XAMLの暗黙スタイルは継承されないため上下中央揃えもここで指定する）
    private static readonly Style HolidayCellStyle = new(typeof(DataGridCell)) {
        Setters = {
            new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE))),
            new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99))),
            new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center)
        }
    };

    // 欠員数（数値）は桁を揃えて比較しやすいよう右寄せ・上下中央にする
    private static readonly Style RightAlignedTextStyle = new(typeof(TextBlock)) {
        Setters = {
            new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right),
            new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center)
        }
    };
    private static readonly Style RightAlignedEditingStyle = new(typeof(TextBox)) {
        Setters = {
            new Setter(TextBox.TextAlignmentProperty, TextAlignment.Right),
            new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center)
        }
    };

    public DepartmentAbsenceSettingWindow() {
        InitializeComponent();

        // 年リストは休日設定画面と同様に当年・翌年のみ
        var today = DateTime.Today;
        CmbYear.ItemsSource = new[] { today.Year, today.Year + 1 };
        CmbYear.SelectedItem = today.Year;
        CmbMonth.ItemsSource = Enumerable.Range(1, 12).ToList();
        CmbMonth.SelectedItem = today.Month;

        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync() {
        _departments = (await SqliteDepartmentRepository.GetAllAsync()).ToList();
        await RebuildGridAsync();
    }

    private async void CmbYearOrMonth_SelectionChanged(object sender, SelectionChangedEventArgs e) => await RebuildGridAsync();

    private async Task RebuildGridAsync() {
        if (CmbYear.SelectedItem is not int year || CmbMonth.SelectedItem is not int month) return;

        var daysInMonth = DateTime.DaysInMonth(year, month);
        var dates = Enumerable.Range(1, daysInMonth).Select(d => new DateOnly(year, month, d)).ToList();

        var holidays = await _holidayRepository.GetByYearAsync(year);
        _holidayDates = holidays.Select(h => h.Date).ToHashSet();

        var absences = await SqliteDepartmentAbsenceRepository.GetByMonthAsync(year, month);
        var absenceMap = absences.ToDictionary(a => (a.DepartmentId, a.Date), a => a.AbsentCount);

        var rows = _departments.Select(d => new DepartmentAbsenceSettingRow {
            DepartmentId = d.Id,
            DepartmentName = d.Name,
            Headcount = d.Headcount,
            Cells = dates.Select(date => new DepartmentAbsenceSettingCell {
                Date = date,
                AbsentCount = absenceMap.GetValueOrDefault((d.Id, date), 0)
            }).ToList()
        }).ToList();

        // awaitを挟む間に年・月が連続変更されても、行データと列数がずれないよう、
        // ここまでローカル変数で計算してから最後にまとめてフィールド・UIへ反映する
        _currentDates = dates;

        // 月替わりで日数が変わるため、日付列は毎回作り直す
        while (AbsenceGrid.Columns.Count > 2)
            AbsenceGrid.Columns.RemoveAt(2);
        for (int i = 0; i < _currentDates.Count; i++)
            AbsenceGrid.Columns.Add(BuildDateColumn(_currentDates[i], i, _holidayDates.Contains(_currentDates[i])));

        AbsenceGrid.ItemsSource = rows;
        TxtStatus.Text = $"{rows.Count} 部署";
    }

    /// <summary>休日列は欠員数の入力対象外のため読み取り専用にし、グレーアウトして区別する</summary>
    private static DataGridTextColumn BuildDateColumn(DateOnly date, int index, bool isHoliday) {
        var column = new DataGridTextColumn {
            Header = date.ToString("M/d"),
            Width = 40,
            Binding = new Binding($"Cells[{index}].AbsentCount"),
            ElementStyle = RightAlignedTextStyle,
            EditingElementStyle = RightAlignedEditingStyle
        };
        if (isHoliday) {
            column.IsReadOnly = true;
            column.CellStyle = HolidayCellStyle;
        }
        return column;
    }

    /// <summary>基本人数列・日付列（欠員数）の編集完了時、DBへ保存する</summary>
    private async void AbsenceGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (e.Row.Item is not DepartmentAbsenceSettingRow row) return;
        if (e.EditingElement is not TextBox textBox) return;

        if (!int.TryParse(textBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) || value < 0) {
            e.Cancel = true;
            MessageBox.Show("0以上の整数で入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var columnIndex = AbsenceGrid.Columns.IndexOf(e.Column);
        try {
            if (columnIndex == 1) {
                await SqliteDepartmentRepository.UpdateHeadcountAsync(row.DepartmentId, value);
                row.Headcount = value;
                var department = _departments.FirstOrDefault(d => d.Id == row.DepartmentId);
                if (department != null) department.Headcount = value;
                return;
            }

            var dateIndex = columnIndex - 2;
            if (dateIndex < 0 || dateIndex >= _currentDates.Count) return;
            await SqliteDepartmentAbsenceRepository.UpsertAsync(row.DepartmentId, _currentDates[dateIndex], value);
        } catch (Exception ex) {
            MessageBox.Show($"保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
