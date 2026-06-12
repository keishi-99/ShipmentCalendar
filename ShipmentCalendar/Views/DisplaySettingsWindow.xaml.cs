using ShipmentCalendar.ViewModels;
using System.Windows;

namespace ShipmentCalendar.Views;

public partial class DisplaySettingsWindow : Window
{
    private readonly MainViewModel _viewModel;

    public DisplaySettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;

        var settings = viewModel.Settings;
        TxtFixedColumnFontSize.Text = settings.FixedColumnFontSize.ToString();
        ChkShowProcessDate.IsChecked = settings.ShowProcessDate;
        ChkShowProcessRequiredHours.IsChecked = settings.ShowProcessRequiredHours;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(TxtFixedColumnFontSize.Text, out var fontSize) || fontSize < 5 || fontSize > 100)
        {
            MessageBox.Show("固定列のフォントサイズには、5から100の間の有効な数値を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _viewModel.Settings.FixedColumnFontSize = fontSize;
        _viewModel.Settings.ShowProcessDate = ChkShowProcessDate.IsChecked == true;
        _viewModel.Settings.ShowProcessRequiredHours = ChkShowProcessRequiredHours.IsChecked == true;

        _viewModel.SaveSettings();
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
