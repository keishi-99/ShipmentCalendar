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
        TxtProcessColumnFontSize.Text = settings.ProcessColumnFontSize.ToString();
        ChkShowProcessDate.IsChecked = settings.ShowProcessDate;
        ChkShowProcessRequiredHours.IsChecked = settings.ShowProcessRequiredHours;
        TxtManualRowHeight.Text = settings.ManualRowHeight.ToString();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(TxtFixedColumnFontSize.Text, out var fixedFontSize) || fixedFontSize < 5 || fixedFontSize > 100)
        {
            MessageBox.Show("固定列のフォントサイズには、5から100の間の有効な数値を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!double.TryParse(TxtProcessColumnFontSize.Text, out var processFontSize) || processFontSize < 5 || processFontSize > 100)
        {
            MessageBox.Show("工程列のフォントサイズには、5から100の間の有効な数値を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!double.TryParse(TxtManualRowHeight.Text, out var manualRowHeight) || manualRowHeight < 0)
        {
            MessageBox.Show("行の高さには0以上の数値を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _viewModel.Settings.FixedColumnFontSize = fixedFontSize;
        _viewModel.Settings.ProcessColumnFontSize = processFontSize;
        _viewModel.Settings.ShowProcessDate = ChkShowProcessDate.IsChecked == true;
        _viewModel.Settings.ShowProcessRequiredHours = ChkShowProcessRequiredHours.IsChecked == true;
        _viewModel.Settings.ManualRowHeight = manualRowHeight;

        _viewModel.SaveSettings();
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
