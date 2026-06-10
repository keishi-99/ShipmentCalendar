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
        TxtOdbcServer.Text = settings.OdbcServer;
        TxtOdbcPort.Text = settings.OdbcPort;
        TxtOdbcDatabase.Text = settings.OdbcDatabase;
        TxtOdbcUserId.Text = settings.OdbcUserId;
        PwdOdbcPassword.Password = settings.OdbcPassword;
        TxtRefreshMinutes.Text = settings.AutoRefreshMinutes.ToString();
        TxtPastDays.Text = settings.DeliveryDatePastDays.ToString();
        TxtRangeDays.Text = settings.DeliveryDateRangeDays.ToString();
        TxtCompletionLeadDays.Text = settings.CompletionDateLeadDays.ToString();
        ChkShowDueDateForNotStarted.IsChecked = settings.ShowDueDateForNotStarted;

        if (settings.OdbcConnectionMode == "Direct")
            RbModeDirect.IsChecked = true;
        else
            RbModeDsn.IsChecked = true;
        UpdateModeFieldsEnabled();
    }

    private void RbMode_Checked(object sender, RoutedEventArgs e) => UpdateModeFieldsEnabled();

    /// <summary>選択中の接続方式に応じて入力欄の有効/無効を切り替える</summary>
    private void UpdateModeFieldsEnabled()
    {
        // RbModeDirectがnullになるのはInitializeComponent完了前のイベント発火を防ぐため
        if (RbModeDirect == null) return;

        var isDirect = RbModeDirect.IsChecked == true;
        TxtOdbcDsn.IsEnabled = !isDirect;
        TxtOdbcServer.IsEnabled = isDirect;
        TxtOdbcPort.IsEnabled = isDirect;
        TxtOdbcDatabase.IsEnabled = isDirect;
    }

    private AppSettings BuildSettingsFromInputs() => new AppSettings
    {
        OdbcConnectionMode = RbModeDirect.IsChecked == true ? "Direct" : "Dsn",
        OdbcDsn = TxtOdbcDsn.Text.Trim(),
        OdbcServer = TxtOdbcServer.Text.Trim(),
        OdbcPort = TxtOdbcPort.Text.Trim(),
        OdbcDatabase = TxtOdbcDatabase.Text.Trim(),
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
        var settings = BuildSettingsFromInputs();
        _viewModel.Settings.OdbcConnectionMode = settings.OdbcConnectionMode;
        _viewModel.Settings.OdbcDsn = settings.OdbcDsn;
        _viewModel.Settings.OdbcServer = settings.OdbcServer;
        _viewModel.Settings.OdbcPort = settings.OdbcPort;
        _viewModel.Settings.OdbcDatabase = settings.OdbcDatabase;
        _viewModel.Settings.OdbcUserId = settings.OdbcUserId;
        _viewModel.Settings.OdbcPassword = settings.OdbcPassword;
        _viewModel.Settings.AutoRefreshMinutes = int.TryParse(TxtRefreshMinutes.Text, out var min) ? min : 5;
        _viewModel.Settings.DeliveryDatePastDays = int.TryParse(TxtPastDays.Text, out var past) ? past : 0;
        _viewModel.Settings.DeliveryDateRangeDays = int.TryParse(TxtRangeDays.Text, out var days) ? days : 90;
        _viewModel.Settings.CompletionDateLeadDays = int.TryParse(TxtCompletionLeadDays.Text, out var leadDays) && leadDays >= 0 && leadDays <= 365 ? leadDays : 1;
        _viewModel.Settings.ShowDueDateForNotStarted = ChkShowDueDateForNotStarted.IsChecked == true;

        _viewModel.SaveSettings();
        DialogResult = true;

        await _viewModel.LoadOrdersAsync();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
