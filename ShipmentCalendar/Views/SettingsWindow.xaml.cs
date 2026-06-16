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
        TxtOdbcUserId.Text = settings.OdbcUserId;
        PwdOdbcPassword.Password = settings.OdbcPassword;
        TxtRefreshMinutes.Text = settings.AutoRefreshMinutes.ToString();
        TxtPastDays.Text = settings.DeliveryDatePastDays.ToString();
        TxtRangeDays.Text = settings.DeliveryDateRangeDays.ToString();
        TxtCompletionLeadDays.Text = settings.CompletionDateLeadDays.ToString();
    }

    private AppSettings BuildSettingsFromInputs() => new()
    {
        OdbcDsn = TxtOdbcDsn.Text.Trim(),
        OdbcUserId = TxtOdbcUserId.Text.Trim(),
        OdbcPassword = PwdOdbcPassword.Password
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
        _viewModel.Settings.OdbcDsn = TxtOdbcDsn.Text.Trim();
        _viewModel.Settings.OdbcUserId = TxtOdbcUserId.Text.Trim();
        _viewModel.Settings.OdbcPassword = PwdOdbcPassword.Password;
        _viewModel.Settings.AutoRefreshMinutes = int.TryParse(TxtRefreshMinutes.Text, out var min) && min >= 0 ? min : 5;
        _viewModel.Settings.DeliveryDatePastDays = int.TryParse(TxtPastDays.Text, out var past) && past >= 0 ? past : 0;
        _viewModel.Settings.DeliveryDateRangeDays = int.TryParse(TxtRangeDays.Text, out var days) && days >= 0 ? days : 90;
        _viewModel.Settings.CompletionDateLeadDays = int.TryParse(TxtCompletionLeadDays.Text, out var leadDays) && leadDays >= 0 && leadDays <= 365 ? leadDays : 1;

        _viewModel.SaveSettings();
        DialogResult = true;

        await _viewModel.LoadOrdersAsync();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
