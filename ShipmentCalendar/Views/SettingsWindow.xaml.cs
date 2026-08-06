using ShipmentCalendar.Models;
using ShipmentCalendar.Services;
using ShipmentCalendar.ViewModels;
using System.IO;
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
        TxtFactoryNumber.Text = settings.OdbcFactoryNumber;
        TxtRefreshMinutes.Text = settings.AutoRefreshMinutes.ToString();
        TxtPastDays.Text = settings.DeliveryDatePastDays.ToString();
        TxtRangeDays.Text = settings.DeliveryDateRangeDays.ToString();
        TxtCompletionLeadDays.Text = settings.CompletionDateLeadDays.ToString();
        TxtDayMinutes.Text = settings.DayMinutes.ToString();
        TxtSharedDataFolderPath.Text = settings.SharedDataFolderPath;
    }

    private AppSettings BuildSettingsFromInputs() => new()
    {
        OdbcDsn = TxtOdbcDsn.Text.Trim(),
        OdbcFactoryNumber = TxtFactoryNumber.Text.Trim()
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
        var errors = new List<string>();

        if (!int.TryParse(TxtRefreshMinutes.Text, out var refreshMinutes) || refreshMinutes < 0)
            errors.Add("自動更新間隔（分）は0以上の整数で入力してください。");
        if (!int.TryParse(TxtPastDays.Text, out var pastDays) || pastDays < 0)
            errors.Add("納期日の表示範囲（過去側）は0以上の整数で入力してください。");
        if (!int.TryParse(TxtRangeDays.Text, out var rangeDays) || rangeDays < 0)
            errors.Add("納期日の表示範囲（未来側）は0以上の整数で入力してください。");
        if (!int.TryParse(TxtCompletionLeadDays.Text, out var leadDays) || leadDays < 0 || leadDays > 365)
            errors.Add("完了日までの営業日数（既定値）は0〜365の整数で入力してください。");
        if (!int.TryParse(TxtDayMinutes.Text, out var dayMinutes) || dayMinutes <= 0 || dayMinutes > 1440)
            errors.Add("1日の稼働時間（分）は1〜1440の整数で入力してください。");

        var newSharedDataFolderPath = TxtSharedDataFolderPath.Text.Trim();
        if (!string.IsNullOrEmpty(newSharedDataFolderPath))
        {
            try
            {
                Directory.CreateDirectory(newSharedDataFolderPath);

                // フォルダの作成に成功しても、権限不足等でファイルの書き込みができない場合があるため、
                // 実際にファイルを作成・削除して書き込み可否まで確認する
                var probePath = Path.Combine(newSharedDataFolderPath, $".write_test_{Guid.NewGuid():N}.tmp");
                using (File.Create(probePath)) { }
                File.Delete(probePath);
            }
            catch (Exception ex)
            {
                errors.Add($"共有データフォルダに書き込めません: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sharedDataFolderPathChanged = newSharedDataFolderPath != _viewModel.Settings.SharedDataFolderPath;

        _viewModel.Settings.OdbcDsn = TxtOdbcDsn.Text.Trim();
        _viewModel.Settings.OdbcFactoryNumber = TxtFactoryNumber.Text.Trim();
        _viewModel.Settings.AutoRefreshMinutes = refreshMinutes;
        _viewModel.Settings.DeliveryDatePastDays = pastDays;
        _viewModel.Settings.DeliveryDateRangeDays = rangeDays;
        _viewModel.Settings.CompletionDateLeadDays = leadDays;
        _viewModel.Settings.DayMinutes = dayMinutes;
        _viewModel.Settings.SharedDataFolderPath = newSharedDataFolderPath;

        _viewModel.SaveSettings();
        DialogResult = true;

        // 共有データフォルダはDatabaseInitializer/EditLockServiceの静的フィールドとして
        // アプリ起動時に一度だけ解決されるため、変更してもこのプロセス内では反映されない。
        // 古い設定のまま操作を続けさせても意味がないため、案内のうえ強制的に終了させる
        // （これから終了するため、ODBCへの受注データ再取得(LoadOrdersAsync)は行わずスキップする）
        if (sharedDataFolderPathChanged)
        {
            MessageBox.Show(
                "共有データフォルダの設定を反映するには、アプリの再起動が必要です。\nOKを押すとアプリを終了しますので、もう一度起動してください。",
                "再起動してください", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
            return;
        }

        await _viewModel.LoadOrdersAsync();
    }

    private void BtnBrowseSharedDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "共有データフォルダを選択",
            InitialDirectory = TxtSharedDataFolderPath.Text
        };
        if (dialog.ShowDialog() != true) return;

        TxtSharedDataFolderPath.Text = dialog.FolderName;
        UpdateSharedDataFolderPathWarning();
    }

    private void TxtSharedDataFolderPath_LostFocus(object sender, RoutedEventArgs e) => UpdateSharedDataFolderPathWarning();

    /// <summary>ドライブレター経由(Z:\...)だとPCごとにマッピング先が異なる可能性があるため、UNCパスでない場合は注意を促す。
    /// 「参照...」での選択時と、テキストボックスへの手入力時の両方から呼ばれる</summary>
    private void UpdateSharedDataFolderPathWarning()
    {
        var path = TxtSharedDataFolderPath.Text.Trim();
        TxtSharedDataFolderStatus.Text = string.IsNullOrEmpty(path) || path.StartsWith(@"\\")
            ? string.Empty
            : "選択したフォルダはUNCパス（\\\\サーバー名\\共有名 の形式）ではありません。他のPCから見ても同じ場所を指すか確認してください。";
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
