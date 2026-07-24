using ShipmentCalendar.Models;
using ShipmentCalendar.Services;
using ShipmentCalendar.ViewModels;
using System.Windows;

namespace ShipmentCalendar.Views;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;

        var settings = viewModel.Settings;
        TxtOdbcDsn.Text = settings.OdbcDsn;
        TxtRefreshMinutes.Text = settings.AutoRefreshMinutes.ToString();
        TxtPastDays.Text = settings.DeliveryDatePastDays.ToString();
        TxtRangeDays.Text = settings.DeliveryDateRangeDays.ToString();
        TxtCompletionLeadDays.Text = settings.CompletionDateLeadDays.ToString();
        TxtDayMinutes.Text = settings.DayMinutes.ToString();
    }

    private AppSettings BuildSettingsFromInputs() => new()
    {
        OdbcDsn = TxtOdbcDsn.Text.Trim()
    };

    private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
    {
        TxtConnectionStatus.Text = "接続中...";
        var settings = BuildSettingsFromInputs();

        var error = await Task.Run(() => OdbcConnectionFactory.Test(settings));
        if (error == null)
        {
            TxtConnectionStatus.Foreground = System.Windows.Media.Brushes.Green;
            TxtConnectionStatus.Text = "接続成功";
        }
        else
        {
            TxtConnectionStatus.Foreground = System.Windows.Media.Brushes.Red;
            TxtConnectionStatus.Text = $"接続失敗：{error}";
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();

        if (!int.TryParse(TxtRefreshMinutes.Text, out var refreshMinutes) || refreshMinutes < 0)
            errors.Add("自動更新間隔（分）は0以上の整数で入力してください。");
        if (!int.TryParse(TxtPastDays.Text, out var pastDays) || pastDays < 0)
            errors.Add("納期日の表示範囲（過去側）は0以上の整数で入力してください。");
        if (!int.TryParse(TxtRangeDays.Text, out var rangeDays) || rangeDays < 0)
            errors.Add("納期日の表示範囲（未来側）は0以上の整数で入力してください。");
        if (!int.TryParse(TxtCompletionLeadDays.Text, out var leadDays) || leadDays < 0 || leadDays > 365)
            errors.Add("完了日までの営業日数（既定値）は0〜365の整数で入力してください。");
        if (!int.TryParse(TxtDayMinutes.Text, out var dayMinutes) || dayMinutes <= 0 || dayMinutes > 1440)
            errors.Add("1日の稼働時間（分）は1〜1440の整数で入力してください。");

        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _viewModel.Settings.OdbcDsn = TxtOdbcDsn.Text.Trim();
        _viewModel.Settings.AutoRefreshMinutes = refreshMinutes;
        _viewModel.Settings.DeliveryDatePastDays = pastDays;
        _viewModel.Settings.DeliveryDateRangeDays = rangeDays;
        _viewModel.Settings.CompletionDateLeadDays = leadDays;
        _viewModel.Settings.DayMinutes = dayMinutes;

        _viewModel.SaveSettings();
        DialogResult = true;

        await _viewModel.LoadOrdersAsync();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
