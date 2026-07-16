using ShipmentCalendar.Models;
using ShipmentCalendar.ViewModels;
using ShipmentCalendar.Views;
using System.Windows;

namespace ShipmentCalendar.Services;

public class DialogService : IDialogService {
    private static Window? Owner => Application.Current.MainWindow;

    public void ShowBasicSettings(MainViewModel viewModel) =>
        new SettingsWindow(viewModel) { Owner = Owner }.ShowDialog();

    public void ShowProcessSetting() =>
        new ProcessSettingWindow() { Owner = Owner }.ShowDialog();

    public void ShowHolidaySetting() =>
        new HolidaySettingWindow() { Owner = Owner }.ShowDialog();

    public void ShowDepartmentSetting() =>
        new DepartmentSettingWindow() { Owner = Owner }.ShowDialog();

    public void ShowProductPerformance(AppSettings settings) =>
        new ProductPerformanceWindow(settings) { Owner = Owner }.ShowDialog();

    public bool? ShowDisplaySettings(MainViewModel viewModel, IDisplaySettingsPreviewTarget previewTarget) {
        var window = new DisplaySettingsWindow(viewModel, previewTarget) { Owner = Owner };
        window.ShowDialog();
        return window.DialogResult;
    }
}
