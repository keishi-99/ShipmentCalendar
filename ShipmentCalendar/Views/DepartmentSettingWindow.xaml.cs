using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using System.Windows;

namespace ShipmentCalendar.Views;

public partial class DepartmentSettingWindow : Window
{
    private readonly SqliteDepartmentRepository _repository = new SqliteDepartmentRepository();
    private List<Department> _departments = new();

    public DepartmentSettingWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _departments = (await _repository.GetAllAsync()).ToList();
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
            await _repository.AddAsync(name);
            TxtDeptName.Text = string.Empty;
            await LoadAsync();
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
            await _repository.DeleteAsync(dept.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"削除エラー: {ex.Message}";
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
