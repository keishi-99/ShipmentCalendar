using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using System.Windows;
using System.Windows.Controls;

namespace ShipmentCalendar.Views;

public partial class HolidaySettingWindow : Window
{
    private readonly IHolidayRepository _repository = new SqliteHolidayRepository();
    private readonly AppSettingsService _settingsService = new AppSettingsService();
    private List<Holiday> _holidays = new();

    public HolidaySettingWindow()
    {
        InitializeComponent();

        // 年リストを現在年±2年で初期化
        var currentYear = DateTime.Today.Year;
        CmbYear.ItemsSource = Enumerable.Range(currentYear - 1, 4).ToList();
        CmbYear.SelectedItem = currentYear;

        TxtFactoryNumber.Text = _settingsService.Load().OdbcFactoryNumber;

        Loaded += async (_, _) => await LoadHolidaysAsync();
    }

    /// <summary>工場番号の入力欄からフォーカスが外れたタイミングで設定を保存する</summary>
    private void TxtFactoryNumber_LostFocus(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Load();
        settings.OdbcFactoryNumber = TxtFactoryNumber.Text.Trim();
        _settingsService.Save(settings);
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

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (DpHoliday.SelectedDate is null)
        {
            TxtStatus.Text = "日付を選択してください";
            return;
        }

        var holiday = new Holiday
        {
            Date = DateOnly.FromDateTime(DpHoliday.SelectedDate.Value),
            Description = TxtDescription.Text.Trim()
        };

        await _repository.AddAsync(holiday);
        TxtDescription.Text = string.Empty;
        DpHoliday.SelectedDate = null;
        await LoadHolidaysAsync();
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (HolidayGrid.SelectedItem is not Holiday selected) return;
        await _repository.DeleteAsync(selected.Id);
        await LoadHolidaysAsync();
    }

    /// <summary>VP_カレンダ情報_YD（稼働区分='01'）から選択中の年の休日を取得してDBに登録する</summary>
    private async void BtnFetchHolidays_Click(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Load();
        if (!settings.IsOdbcConfigured)
        {
            TxtStatus.Text = "設定からODBC接続情報を入力してください";
            return;
        }
        if (string.IsNullOrEmpty(settings.OdbcFactoryNumber))
        {
            TxtStatus.Text = "設定から工場番号を入力してください";
            return;
        }

        var year = (int)(CmbYear.SelectedItem ?? DateTime.Today.Year);
        TxtStatus.Text = "休日データを取得中...";

        try
        {
            var calendarRepo = new OdbcCalendarRepository(settings);
            var dates = await Task.Run(async () => await calendarRepo.GetHolidaysAsync(year));

            var added = 0;
            var skipped = 0;
            var existing = (await _repository.GetAllAsync())
                .Select(h => h.Date)
                .ToHashSet();

            foreach (var date in dates)
            {
                if (existing.Contains(date)) { skipped++; continue; }

                await _repository.AddAsync(new Holiday
                {
                    Date = date,
                    Description = string.Empty
                });
                existing.Add(date);
                added++;
            }

            await LoadHolidaysAsync();
            TxtStatus.Text = $"取得完了：{added} 件追加、{skipped} 件スキップ（登録済み）";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"取得失敗：{ex.Message}";
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
