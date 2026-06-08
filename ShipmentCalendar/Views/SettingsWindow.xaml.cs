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

        TxtOdbcDsn.Text = viewModel.Settings.OdbcDsn;
        TxtOdbcUserId.Text = viewModel.Settings.OdbcUserId;
        PwdOdbcPassword.Password = viewModel.Settings.OdbcPassword;
        TxtRefreshMinutes.Text = viewModel.Settings.AutoRefreshMinutes.ToString();
        TxtPastDays.Text = viewModel.Settings.DeliveryDatePastDays.ToString();
        TxtRangeDays.Text = viewModel.Settings.DeliveryDateRangeDays.ToString();
    }

    private void BtnTestConnection_Click(object sender, RoutedEventArgs e)
    {
        TxtConnectionStatus.Text = "接続中...";
        var error = OdbcConnectionFactory.Test(TxtOdbcDsn.Text.Trim(), TxtOdbcUserId.Text.Trim(), PwdOdbcPassword.Password);
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
        _viewModel.Settings.OdbcDsn = TxtOdbcDsn.Text.Trim();
        _viewModel.Settings.OdbcUserId = TxtOdbcUserId.Text.Trim();
        _viewModel.Settings.OdbcPassword = PwdOdbcPassword.Password;
        _viewModel.Settings.AutoRefreshMinutes = int.TryParse(TxtRefreshMinutes.Text, out var min) ? min : 5;
        _viewModel.Settings.DeliveryDatePastDays = int.TryParse(TxtPastDays.Text, out var past) ? past : 0;
        _viewModel.Settings.DeliveryDateRangeDays = int.TryParse(TxtRangeDays.Text, out var days) ? days : 90;

        _viewModel.SaveSettings();
        DialogResult = true;

        await _viewModel.LoadOrdersAsync();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
