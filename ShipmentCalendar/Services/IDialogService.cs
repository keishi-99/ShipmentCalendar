using ShipmentCalendar.Models;
using ShipmentCalendar.ViewModels;

namespace ShipmentCalendar.Services;

/// <summary>設定系ダイアログの表示を担う。MainViewModelはWindowを直接参照せずこのインターフェース経由でダイアログを開く</summary>
public interface IDialogService {
    void ShowBasicSettings(MainViewModel viewModel);
    void ShowProcessSetting();
    void ShowHolidaySetting();
    void ShowDepartmentSetting();
    void ShowProductPerformance(AppSettings settings);
    void ShowDepartmentLoad(IEnumerable<Order> orders, AppSettings settings);

    /// <summary>表示設定ダイアログを表示する。保存されたか（DialogResult）を返す</summary>
    bool? ShowDisplaySettings(MainViewModel viewModel, IDisplaySettingsPreviewTarget previewTarget);
}
