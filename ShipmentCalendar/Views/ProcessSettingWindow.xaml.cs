using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShipmentCalendar.Views;

/// <summary>DepartmentId → 部署名に変換するコンバーター（ProcessSettingWindow 用）</summary>
public class DeptIdToNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int id && id > 0)
        {
            var dept = ProcessSettingWindow.DepartmentsSource?.FirstOrDefault(d => d.Id == id);
            return dept?.Name ?? string.Empty;
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public partial class ProcessSettingWindow : Window
{
    private readonly IProcessDefinitionRepository _dbRepository = new SqliteProcessDefinitionRepository();
    private readonly AppSettingsService _settingsService = new AppSettingsService();
    private readonly SqliteProductDisplayNameRepository _nameRepository = new SqliteProductDisplayNameRepository();
    private ObservableCollection<ProcessDefinition> _currentDefinitions = new();

    /// <summary>XAML の DataTemplate から参照できる静的な部署リスト</summary>
    public static IReadOnlyList<Department>? DepartmentsSource { get; private set; }

    public ProcessSettingWindow()
    {
        InitializeComponent();
        ProcessGrid.ItemsSource = _currentDefinitions;
        Loaded += async (_, _) =>
        {
            // 部署リストをDBから読み込んでDataGrid.Tag経由でCellEditingTemplateに渡す
            var depts = (await new SqliteDepartmentRepository().GetAllAsync()).ToList();
            // 先頭に「未設定」（Id=0）を追加
            var allDepts = new List<Department> { new Department { Id = 0, Name = "（未設定）" } };
            allDepts.AddRange(depts);
            DepartmentsSource = allDepts;
            ProcessGrid.Tag = allDepts;

            await RefreshRegisteredListAsync();
        };
    }

    /// <summary>DB に登録済みの品目番号一覧を保持する（一覧選択ウィンドウ用）</summary>
    private List<ItemPickerEntry> _registeredItems = new();

    /// <summary>DB に登録済みの品目番号一覧を読み込む</summary>
    private async Task RefreshRegisteredListAsync()
    {
        var itemNumbers = (await _dbRepository.GetItemNumbersAsync())
            .OrderBy(n => n)
            .ToList();
        var displayNames = await _nameRepository.GetAllDisplayNamesAsync();

        _registeredItems = itemNumbers
            .Select(n => new ItemPickerEntry
            {
                ItemNumber = n,
                DisplayName = displayNames.TryGetValue(n, out var name) ? name : string.Empty
            })
            .ToList();
    }

    /// <summary>「一覧から選択」ボタン。検索ボックス付きの一覧から品目番号を選択する</summary>
    private async void BtnSelectItem_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ItemNumberPickerWindow(_registeredItems) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedItemNumber is not string itemNumber) return;

        await LoadRegisteredItemAsync(itemNumber);
    }

    /// <summary>選択した品目番号の内容を入力欄・グリッドに表示する</summary>
    private async Task LoadRegisteredItemAsync(string itemNumber)
    {
        TxtItemNumber.Text = itemNumber;
        TxtRegisteredDisplay.Text = itemNumber;

        // DB登録済みの品目名を表示
        TxtItemName.Text = await _nameRepository.GetDisplayNameAsync(itemNumber) ?? string.Empty;

        // DB設定をそのままグリッドに表示（CSVとのマージは行わず登録内容を確認）
        var dbDefs = (await _dbRepository.GetByItemNumberAsync(itemNumber))
            .OrderBy(d => d.SortOrder)
            .ToList();
        _currentDefinitions = new System.Collections.ObjectModel.ObservableCollection<ProcessDefinition>(dbDefs);
        ProcessGrid.ItemsSource = _currentDefinitions;
        TxtStatus.Text = $"{dbDefs.Count} 件の登録済み工程を表示しています";
    }

    /// <summary>
    /// 「CSVから取り込む」ボタン。
    /// VP_指示工程情報_YD から品目番号で工程を取得し、DB既存設定をマージしてグリッドに表示する。
    /// </summary>
    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var itemNumber = TxtItemNumber.Text.Trim();
        if (string.IsNullOrEmpty(itemNumber))
        {
            TxtStatus.Text = "品目番号を入力してください";
            return;
        }

        var settings = _settingsService.Load();
        if (!settings.IsOdbcConfigured)
        {
            TxtStatus.Text = "設定からODBC接続情報を入力してください";
            return;
        }

        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            // ODBCから工程定義を取得（順序・指示先番号・デフォルト工程名・LT）
            // ODBC呼び出しは実質同期処理のため、UIスレッドのフリーズを避けてバックグラウンドで実行する
            var odbcRepo = new OdbcProcessDefinitionRepository(settings);
            var odbcDefs = (await Task.Run(async () => await odbcRepo.GetByItemNumberAsync(itemNumber))).ToList();

            if (!odbcDefs.Any())
            {
                TxtStatus.Text = $"品目番号 '{itemNumber}' の工程データが見つかりませんでした";
                return;
            }

            // 品目名の初期値をDB未登録ならODBCから取得
            if (string.IsNullOrEmpty(TxtItemName.Text))
            {
                var dbItemName = await Task.Run(async () => await LookupItemNameFromOdbcAsync(itemNumber, settings));
                if (!string.IsNullOrEmpty(dbItemName))
                    TxtItemName.Text = dbItemName;
            }

            // DB既存設定を取得（品目番号 = ProductName として保存済みのもの）
            var dbDefs = (await _dbRepository.GetByItemNumberAsync(itemNumber)).ToList();
            var dbDict = dbDefs
                .Where(d => !string.IsNullOrEmpty(d.DestinationCode))
                .GroupBy(d => d.DestinationCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // ODBC構造 + DB設定をマージ
            // 順序・指示先番号はODBCが正、工程名・LT・表示・警告はDB設定を優先
            _currentDefinitions = new ObservableCollection<ProcessDefinition>(
                odbcDefs.Select(odbcDef =>
                {
                    if (!dbDict.TryGetValue(odbcDef.DestinationCode, out var db))
                        return odbcDef;  // DB未登録 → ODBC既定値をそのまま使用

                    return new ProcessDefinition
                    {
                        ItemNumber = itemNumber,
                        ProcessName = db.ProcessName,
                        DestinationCode = odbcDef.DestinationCode,
                        SortOrder = odbcDef.SortOrder,                          // 順序は常にODBC
                        LeadTimeMinutes = db.LeadTimeMinutes,
                        IsVisible = db.IsVisible,
                        WarningDaysBeforeDeadline = db.WarningDaysBeforeDeadline,
                        DepartmentId = db.DepartmentId,
                        CoolTimeMinutes = db.CoolTimeMinutes,
                        OutsourceLeadDays = db.OutsourceLeadDays
                    };
                })
            );
            ProcessGrid.ItemsSource = _currentDefinitions;
            TxtStatus.Text = $"{odbcDefs.Count} 件の工程を取り込みました";
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 「保存」ボタン。現在のグリッド内容を DB に保存する（ProductName = 品目番号）。
    /// </summary>
    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var itemNumber = TxtItemNumber.Text.Trim();
        if (string.IsNullOrEmpty(itemNumber))
        {
            TxtStatus.Text = "品目番号を入力してください";
            return;
        }

        // 品目名を保存（工程の有無に関わらず保存する）
        await _nameRepository.SaveDisplayNameAsync(itemNumber, TxtItemName.Text.Trim());

        // グリッドに工程がある場合のみ工程定義を更新する
        if (_currentDefinitions.Any())
        {
            var existing = await _dbRepository.GetByItemNumberAsync(itemNumber);
            foreach (var def in existing)
                await _dbRepository.DeleteAsync(def.Id);

            foreach (var def in _currentDefinitions)
            {
                def.ItemNumber = itemNumber;
                await _dbRepository.AddAsync(def);
            }
            TxtStatus.Text = $"保存しました（品目名 + {_currentDefinitions.Count} 件の工程）";
        }
        else
        {
            TxtStatus.Text = "品目名を保存しました";
        }

        // 登録済みリストを更新
        await RefreshRegisteredListAsync();
        TxtRegisteredDisplay.Text = itemNumber;
    }

    /// <summary>VP_生産計画情報_YD から品目番号に対応する品目名をODBC経由で取得する</summary>
    private static async Task<string?> LookupItemNameFromOdbcAsync(string itemNumber, Models.AppSettings settings)
    {
        if (!settings.IsOdbcConfigured) return null;

        try
        {
            using var conn = Services.OdbcConnectionFactory.Create(settings);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TOP 1 品目名 FROM VP_生産計画情報_YD WHERE 品目番号 = ?";
            cmd.Parameters.Add("@in", System.Data.Odbc.OdbcType.VarChar).Value = itemNumber;

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString()?.Trim();
        }
        catch { return null; }
    }

    /// <summary>品目番号ごと削除：工程定義と品目名をまとめてDBから削除する</summary>
    private async void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
    {
        var itemNumber = TxtItemNumber.Text.Trim();
        if (string.IsNullOrEmpty(itemNumber))
        {
            TxtStatus.Text = "削除する品目番号を選択または入力してください";
            return;
        }

        var result = MessageBox.Show(
            $"品目番号 '{itemNumber}' の工程設定と品目名をすべて削除します。よろしいですか？",
            "削除確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var existing = await _dbRepository.GetByItemNumberAsync(itemNumber);
        foreach (var def in existing)
            await _dbRepository.DeleteAsync(def.Id);

        // Productsテーブルから品目名も削除
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(ShipmentCalendar.Data.DatabaseInitializer.ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Products WHERE ItemNumber = $in";
        cmd.Parameters.AddWithValue("$in", itemNumber);
        await cmd.ExecuteNonQueryAsync();

        _currentDefinitions.Clear();
        TxtItemName.Text = string.Empty;
        TxtStatus.Text = $"品目番号 '{itemNumber}' を削除しました";

        await RefreshRegisteredListAsync();
        TxtRegisteredDisplay.Text = string.Empty;
        TxtItemNumber.Text = string.Empty;
    }

    /// <summary>グリッド行の削除ボタン：選択行をコレクションから除去する（保存まで確定しない）</summary>
    private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProcessDefinition def)
            _currentDefinitions.Remove(def);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
