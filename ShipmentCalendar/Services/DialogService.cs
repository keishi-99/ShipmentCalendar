using ShipmentCalendar.Models;
using ShipmentCalendar.ViewModels;
using ShipmentCalendar.Views;
using System.Windows;

namespace ShipmentCalendar.Services;

public class DialogService : IDialogService {
    private static Window? Owner => Application.Current?.MainWindow;

    public void ShowBasicSettings(MainViewModel viewModel) =>
        new SettingsWindow(viewModel) { Owner = Owner }.ShowDialog();

    public void ShowProcessSetting() =>
        ShowWithEditLock(() => new ProcessSettingWindow());

    public void ShowHolidaySetting() =>
        ShowWithEditLock(() => new HolidaySettingWindow());

    public void ShowDepartmentSetting() =>
        ShowWithEditLock(() => new DepartmentSettingWindow());

    public void ShowDepartmentAbsenceSetting() =>
        ShowWithEditLock(() => new DepartmentAbsenceSettingWindow());

    /// <summary>マスタDBを編集する画面を、共有編集ロックを取得した状態でモーダル表示する。
    /// 他PCが編集中でロックを取得できない場合は警告のみ表示して画面は開かない。
    /// 表示中は一定間隔でロックのハートビートを更新し、長時間の編集がタイムアウトで奪われないようにする</summary>
    private void ShowWithEditLock(Func<Window> createWindow) {
        var result = EditLockService.TryAcquire();
        if (!result.Acquired) {
            MessageBox.Show(result.HeldByMessage, "編集中のため開けません", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var heartbeatTimer = new System.Windows.Threading.DispatcherTimer {
            Interval = TimeSpan.FromMinutes(5)
        };
        heartbeatTimer.Tick += (_, _) => EditLockService.RefreshHeartbeat();
        heartbeatTimer.Start();

        try {
            var window = createWindow();
            window.Owner = Owner;
            window.ShowDialog();
        } finally {
            heartbeatTimer.Stop();
            EditLockService.Release();
        }
    }

    public void ShowProductPerformance(AppSettings settings) =>
        new ProductPerformanceWindow(settings) { Owner = Owner }.ShowDialog();

    public void ShowDepartmentLoad(IEnumerable<Order> orders, ProductCategoryClassifier categoryClassifier, AppSettings settings) =>
        new DepartmentLoadWindow(orders, categoryClassifier, settings) { Owner = Owner }.ShowDialog();

    public void ShowProcessBottleneck(AppSettings settings) =>
        new ProcessBottleneckWindow(settings) { Owner = Owner }.ShowDialog();

    public bool? ShowDisplaySettings(MainViewModel viewModel, IDisplaySettingsPreviewTarget previewTarget) {
        var window = new DisplaySettingsWindow(viewModel, previewTarget) { Owner = Owner };
        window.ShowDialog();
        return window.DialogResult;
    }
}
