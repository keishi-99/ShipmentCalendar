using CommunityToolkit.Mvvm.ComponentModel;

namespace ShipmentCalendar.ViewModels;

/// <summary>ツールバーの担当部署フィルターボタン1個分のデータ</summary>
public partial class DepartmentFilterItem : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
