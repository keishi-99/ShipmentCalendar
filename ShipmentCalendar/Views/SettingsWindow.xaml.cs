using Microsoft.Win32;
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

        TxtSeisanKeikaku.Text = viewModel.Settings.SeisanKeikakuCsvPath;
        TxtUkeireJisseki.Text = viewModel.Settings.UkeireJissekiCsvPath;
        TxtShijiKotei.Text = viewModel.Settings.ShijiKoteiCsvPath;
        TxtMeishoJoho.Text = viewModel.Settings.MeishoJohoCsvPath;
        TxtRefreshMinutes.Text = viewModel.Settings.AutoRefreshMinutes.ToString();
        TxtPastDays.Text = viewModel.Settings.DeliveryDatePastDays.ToString();
        TxtRangeDays.Text = viewModel.Settings.DeliveryDateRangeDays.ToString();
    }

    private void BtnBrowseSeisanKeikaku_Click(object sender, RoutedEventArgs e)
        => Browse("生産計画CSV（VP_生産計画情報_YD）を選択", t => TxtSeisanKeikaku.Text = t);

    private void BtnBrowseUkeireJisseki_Click(object sender, RoutedEventArgs e)
        => Browse("受入実績CSV（VP_受入実績情報_YD）を選択", t => TxtUkeireJisseki.Text = t);

    private void BtnBrowseShijiKotei_Click(object sender, RoutedEventArgs e)
        => Browse("指示工程CSV（VP_指示工程情報_YD）を選択", t => TxtShijiKotei.Text = t);

    private void BtnBrowseMeishoJoho_Click(object sender, RoutedEventArgs e)
        => Browse("名称情報CSV（VP_名称情報_YD）を選択", t => TxtMeishoJoho.Text = t);

    private static void Browse(string title, Action<string> setter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSVファイル (*.csv;*.txt)|*.csv;*.txt|すべてのファイル (*.*)|*.*",
            Title = title
        };
        if (dialog.ShowDialog() == true)
            setter(dialog.FileName);
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Settings.SeisanKeikakuCsvPath = TxtSeisanKeikaku.Text.Trim();
        _viewModel.Settings.UkeireJissekiCsvPath = TxtUkeireJisseki.Text.Trim();
        _viewModel.Settings.ShijiKoteiCsvPath = TxtShijiKotei.Text.Trim();
        _viewModel.Settings.MeishoJohoCsvPath = TxtMeishoJoho.Text.Trim();
        _viewModel.Settings.AutoRefreshMinutes = int.TryParse(TxtRefreshMinutes.Text, out var min) ? min : 5;
        _viewModel.Settings.DeliveryDatePastDays = int.TryParse(TxtPastDays.Text, out var past) ? past : 0;
        _viewModel.Settings.DeliveryDateRangeDays = int.TryParse(TxtRangeDays.Text, out var days) ? days : 90;

        _viewModel.SaveSettings();
        DialogResult = true;

        await _viewModel.LoadOrdersAsync();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
