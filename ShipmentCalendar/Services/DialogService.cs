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
        ShowWithEditLock("process_setting", () => new ProcessSettingWindow());

    public void ShowHolidaySetting() =>
        ShowWithEditLock("holiday_setting", () => new HolidaySettingWindow());

    public void ShowDepartmentSetting() =>
        ShowWithEditLock("department_setting", () => new DepartmentSettingWindow());

    public void ShowDepartmentAbsenceSetting() =>
        ShowWithEditLock("department_absence_setting", () => new DepartmentAbsenceSettingWindow());

    /// <summary>マスタDBを編集する画面を、共有編集ロックを取得した状態でモーダル表示する。
    /// ロックはlockNameごとに独立しているため、他の画面のロックには影響しない。
    /// 他PCが同じ画面を編集中でロックを取得できない場合は警告のみ表示して画面は開かない。
    /// 表示中は一定間隔でロックのハートビートを更新し、長時間の編集がタイムアウトで奪われないようにする</summary>
    private void ShowWithEditLock(string lockName, Func<Window> createWindow) {
        var result = EditLockService.TryAcquire(lockName);
        if (!result.Acquired) {
            MessageBox.Show(result.HeldByMessage, "開けません", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = createWindow();
        window.Owner = Owner;

        var heartbeatTimer = new System.Windows.Threading.DispatcherTimer {
            Interval = TimeSpan.FromMinutes(5)
        };
        heartbeatTimer.Tick += (_, _) => {
            if (EditLockService.RefreshHeartbeat(lockName)) return;

            // 他PCに正当に引き継がれてしまった場合、気づかず編集を続けさせないよう画面を強制的に閉じる
            heartbeatTimer.Stop();
            MessageBox.Show(
                "編集ロックが失われたため、この画面を閉じます（他のPCに引き継がれた可能性があります）。",
                "編集ロックが失われました", MessageBoxButton.OK, MessageBoxImage.Warning);
            window.Close();
        };
        heartbeatTimer.Start();

        try {
            window.ShowDialog();
        } finally {
            heartbeatTimer.Stop();
            EditLockService.Release(lockName);
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
