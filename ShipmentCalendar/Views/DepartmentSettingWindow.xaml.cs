using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace ShipmentCalendar.Views;

public partial class DepartmentSettingWindow : Window
{
    private List<Department> _departments = [];

    public DepartmentSettingWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _departments = (await SqliteDepartmentRepository.GetAllAsync()).ToList();
        DeptGrid.ItemsSource = _departments;
        TxtStatus.Text = $"{_departments.Count} 件";
    }

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtDeptName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            TxtStatus.Text = "部署名を入力してください";
            return;
        }

        try
        {
            var added = await SqliteDepartmentRepository.AddAsync(name);
            TxtDeptName.Text = string.Empty;
            await LoadAsync();
            TxtStatus.Text = added ? string.Empty : $"「{name}」は既に登録されています";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"追加エラー: {ex.Message}";
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Department dept) return;

        var result = MessageBox.Show(
            $"部署「{dept.Name}」を削除しますか？\n工程設定の担当部署は「未設定」になります。",
            "削除確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await SqliteDepartmentRepository.DeleteAsync(dept.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"削除エラー: {ex.Message}";
        }
    }

    /// <summary>「順序」列の編集完了時、DBへ保存して並び順を再読込する</summary>
    private async void DeptGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (e.Row.Item is not Department dept) return;
        if (e.EditingElement is not TextBox textBox) return;

        if (!int.TryParse(textBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var sortOrder))
        {
            e.Cancel = true;
            MessageBox.Show("整数で入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await SqliteDepartmentRepository.UpdateSortOrderAsync(dept.Id, sortOrder);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"保存エラー: {ex.Message}";
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
