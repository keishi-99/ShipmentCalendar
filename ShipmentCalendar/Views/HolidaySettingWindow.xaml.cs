using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using System.Windows;
using System.Windows.Controls;

namespace ShipmentCalendar.Views;

public partial class HolidaySettingWindow : Window
{
    private readonly SqliteHolidayRepository _repository = new();
    private List<Holiday> _holidays = [];
    private bool _isFetching;

    public HolidaySettingWindow()
    {
        InitializeComponent();

        // 年リストは自動同期・再取得の対象範囲と合わせて当年・翌年のみ
        var currentYear = DateTime.Today.Year;
        CmbYear.ItemsSource = new[] { currentYear, currentYear + 1 };
        CmbYear.SelectedItem = currentYear;

        Loaded += async (_, _) => await LoadHolidaysAsync();
    }

    private async Task LoadHolidaysAsync()
    {
        var year = (int)(CmbYear.SelectedItem ?? DateTime.Today.Year);
        _holidays = (await _repository.GetByYearAsync(year)).ToList();
        HolidayGrid.ItemsSource = _holidays;
        TxtStatus.Text = $"{_holidays.Count} 件の休日";
    }

    private async void CmbYear_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => await LoadHolidaysAsync();

    /// <summary>VP_カレンダ情報_YD（稼働区分='01'）から当年・翌年の休日を再取得し、Holidaysへ反映する</summary>
    private async void BtnRefetch_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsService.Load();
        if (!settings.IsOdbcConfigured)
        {
            TxtStatus.Text = "設定 > 基本設定 からODBC接続情報を入力してください";
            return;
        }
        if (string.IsNullOrEmpty(settings.OdbcFactoryNumber))
        {
            TxtStatus.Text = "設定 > 基本設定 から工場番号を入力してください";
            return;
        }

        BtnRefetch.IsEnabled = false;
        BtnClose.IsEnabled = false;
        CmbYear.IsEnabled = false;
        _isFetching = true;
        TxtStatus.Text = "休日データを再取得中...";

        try
        {
            await OdbcCalendarRepository.SyncCurrentAndNextYearAsync(settings, _repository);
            await LoadHolidaysAsync();
            TxtStatus.Text = $"再取得完了：{_holidays.Count} 件の休日";
        }
        catch (Exception ex)
        {
            // 当年は成功・翌年は失敗のような部分失敗でもDB更新済みの内容をグリッドへ反映する
            await LoadHolidaysAsync();
            TxtStatus.Text = $"再取得失敗：{ex.Message}";
        }
        finally
        {
            BtnRefetch.IsEnabled = true;
            BtnClose.IsEnabled = true;
            CmbYear.IsEnabled = true;
            _isFetching = false;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isFetching)
        {
            e.Cancel = true;
            TxtStatus.Text = "休日データの再取得が完了するまで閉じることができません";
        }
    }
}
