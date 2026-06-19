using ShipmentCalendar.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace ShipmentCalendar.Views;

public partial class DisplaySettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly MainWindow _mainWindow;
    private bool _isInitializing = true;
    // キャンセル時に復元するための保存値
    private readonly double _savedFixedFontSize;
    private readonly double _savedProcessBarFontSize;
    private readonly double _savedProcessColumnFontSize;
    private readonly double _savedManualRowHeight;

    public DisplaySettingsWindow(MainViewModel viewModel, MainWindow mainWindow)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _mainWindow = mainWindow;

        var settings = viewModel.Settings;
        _savedFixedFontSize = settings.FixedColumnFontSize;
        _savedProcessBarFontSize = settings.ProcessBarFontSize;
        _savedProcessColumnFontSize = settings.ProcessColumnFontSize;
        _savedManualRowHeight = settings.ManualRowHeight;

        TxtFixedColumnFontSize.Text = settings.FixedColumnFontSize.ToString();
        TxtProcessBarFontSize.Text = settings.ProcessBarFontSize.ToString();
        TxtProcessColumnFontSize.Text = settings.ProcessColumnFontSize.ToString();
        ChkShowProcessDate.IsChecked = settings.ShowProcessDate;
        ChkShowProcessRequiredHours.IsChecked = settings.ShowProcessRequiredHours;
        TxtManualRowHeight.Text = settings.ManualRowHeight.ToString();
        _isInitializing = false;
    }

    private void TxtFixedFontSize_TextChanged(object sender, TextChangedEventArgs e) {
        if (_isInitializing) return;
        if (double.TryParse(TxtFixedColumnFontSize.Text, out var size) && size >= 5 && size <= 100)
            _mainWindow.PreviewFontSizes(size, 0);
    }

    private void TxtProcessBarFontSize_TextChanged(object sender, TextChangedEventArgs e) {
        if (_isInitializing) return;
        if (double.TryParse(TxtProcessBarFontSize.Text, out var size) && size >= 5 && size <= 100)
            _mainWindow.PreviewFontSizes(0, size);
    }

    private void TxtProcessColumnFontSize_TextChanged(object sender, TextChangedEventArgs e) {
        if (_isInitializing) return;
        if (double.TryParse(TxtProcessColumnFontSize.Text, out var size) && size >= 5 && size <= 100)
            _mainWindow.PreviewFontSizes(0, 0, size);
    }

    private void TxtManualRowHeight_TextChanged(object sender, TextChangedEventArgs e) {
        if (_isInitializing) return;
        var text = TxtManualRowHeight.Text;
        if (string.IsNullOrEmpty(text))
            _mainWindow.PreviewRowHeight(0);
        else if (double.TryParse(text, out var h) && h >= 0)
            _mainWindow.PreviewRowHeight(h);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(TxtFixedColumnFontSize.Text, out var fixedFontSize) || fixedFontSize < 5 || fixedFontSize > 100)
        {
            MessageBox.Show("固定列のフォントサイズには、5から100の間の有効な数値を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!double.TryParse(TxtProcessBarFontSize.Text, out var processBarFontSize) || processBarFontSize < 5 || processBarFontSize > 100)
        {
            MessageBox.Show("工程バーのフォントサイズには、5から100の間の有効な数値を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!double.TryParse(TxtProcessColumnFontSize.Text, out var processFontSize) || processFontSize < 5 || processFontSize > 100)
        {
            MessageBox.Show("工程列のフォントサイズには、5から100の間の有効な数値を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        double manualRowHeight = 0;
        if (!string.IsNullOrEmpty(TxtManualRowHeight.Text) && (!double.TryParse(TxtManualRowHeight.Text, out manualRowHeight) || manualRowHeight < 0))
        {
            MessageBox.Show("行の高さには0以上の数値を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _viewModel.Settings.FixedColumnFontSize = fixedFontSize;
        _viewModel.Settings.ProcessBarFontSize = processBarFontSize;
        _viewModel.Settings.ProcessColumnFontSize = processFontSize;
        _viewModel.Settings.ShowProcessDate = ChkShowProcessDate.IsChecked == true;
        _viewModel.Settings.ShowProcessRequiredHours = ChkShowProcessRequiredHours.IsChecked == true;
        _viewModel.Settings.ManualRowHeight = manualRowHeight;

        _viewModel.SaveSettings();
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
    }

    protected override void OnClosed(EventArgs e) {
        base.OnClosed(e);
        if (DialogResult != true) {
            _mainWindow.PreviewFontSizes(_savedFixedFontSize, _savedProcessBarFontSize, _savedProcessColumnFontSize);
            _mainWindow.PreviewRowHeight(_savedManualRowHeight);
        }
    }
}
