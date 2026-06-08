using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace ShipmentCalendar.Views;

public partial class HolidaySettingWindow : Window
{
    private readonly IHolidayRepository _repository = new SqliteHolidayRepository();
    private List<Holiday> _holidays = new();

    public HolidaySettingWindow()
    {
        InitializeComponent();

        // 年リストを現在年±2年で初期化
        var currentYear = DateTime.Today.Year;
        CmbYear.ItemsSource = Enumerable.Range(currentYear - 1, 4).ToList();
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

    /// <summary>内閣府公開CSVから祝日を取得してDBに登録する</summary>
    private async void BtnFetchHolidays_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "祝日データを取得中...";

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var bytes = await client.GetByteArrayAsync(
                "https://www8.cao.go.jp/chosei/shukujitsu/syukujitsu.csv");

            // 内閣府CSVはShift-JIS
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var csv = Encoding.GetEncoding("shift_jis").GetString(bytes);

            var added = 0;
            var skipped = 0;
            var existing = (await _repository.GetAllAsync())
                .Select(h => h.Date)
                .ToHashSet();

            foreach (var line in csv.Split('\n').Skip(1))
            {
                var cols = line.Trim().Split(',');
                if (cols.Length < 2) continue;
                if (!DateOnly.TryParseExact(cols[0].Trim(), "yyyy/M/d",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var date)) continue;

                if (existing.Contains(date)) { skipped++; continue; }

                await _repository.AddAsync(new Holiday
                {
                    Date = date,
                    Description = cols[1].Trim()
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
