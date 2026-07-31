using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ShipmentCalendar.Views;

public partial class DepartmentAbsenceSettingWindow : Window {
    private List<Department> _departments = [];
    private List<DateOnly> _currentDates = [];

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
        _currentDates = Enumerable.Range(1, daysInMonth).Select(d => new DateOnly(year, month, d)).ToList();

        var absences = await SqliteDepartmentAbsenceRepository.GetByMonthAsync(year, month);
        var absenceMap = absences.ToDictionary(a => (a.DepartmentId, a.Date), a => a.AbsentCount);

        var rows = _departments.Select(d => new DepartmentAbsenceSettingRow {
            DepartmentId = d.Id,
            DepartmentName = d.Name,
            Headcount = d.Headcount,
            Cells = _currentDates.Select(date => new DepartmentAbsenceSettingCell {
                Date = date,
                AbsentCount = absenceMap.GetValueOrDefault((d.Id, date), 0)
            }).ToList()
        }).ToList();

        // 月替わりで日数が変わるため、日付列は毎回作り直す
        while (AbsenceGrid.Columns.Count > 2)
            AbsenceGrid.Columns.RemoveAt(2);
        for (int i = 0; i < _currentDates.Count; i++)
            AbsenceGrid.Columns.Add(BuildDateColumn(_currentDates[i], i));

        AbsenceGrid.ItemsSource = rows;
        TxtStatus.Text = $"{rows.Count} 部署";
    }

    private static DataGridTextColumn BuildDateColumn(DateOnly date, int index) => new() {
        Header = date.ToString("M/d"),
        Width = 40,
        Binding = new Binding($"Cells[{index}].AbsentCount")
    };

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
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
