using ShipmentCalendar.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace ShipmentCalendar.Views;

public partial class DisplaySettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly MainWindow _mainWindow;

    public DisplaySettingsWindow(MainViewModel viewModel, MainWindow mainWindow)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _mainWindow = mainWindow;

        var settings = viewModel.Settings;
        TxtFixedColumnFontSize.Text = settings.FixedColumnFontSize.ToString();
        TxtProcessColumnFontSize.Text = settings.ProcessColumnFontSize.ToString();
        ChkShowProcessDate.IsChecked = settings.ShowProcessDate;
        ChkShowProcessRequiredHours.IsChecked = settings.ShowProcessRequiredHours;
        TxtManualRowHeight.Text = settings.ManualRowHeight.ToString();
    }

    private void TxtFontSize_TextChanged(object sender, TextChangedEventArgs e) {
        if (double.TryParse(TxtFixedColumnFontSize.Text, out var size) && size >= 5 && size <= 100)
            _mainWindow.PreviewRowHeight(0); // フォントサイズ変更時は行高さを再計算
    }

    private void TxtManualRowHeight_TextChanged(object sender, TextChangedEventArgs e) {
        if (double.TryParse(TxtManualRowHeight.Text, out var h) && h >= 0)
            _mainWindow.PreviewRowHeight(h);
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

    private void BtnCancel_Click(object sender, RoutedEventArgs e) {
        var s = _viewModel.Settings;
        _mainWindow.PreviewRowHeight(s.ManualRowHeight);
        DialogResult = false;
    }
}
