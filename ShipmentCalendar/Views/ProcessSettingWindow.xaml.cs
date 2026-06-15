using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly IModelCodeRepository _modelCodeRepository = new SqliteModelCodeRepository();
    private ObservableCollection<ProcessDefinition> _currentDefinitions = new();
    private ObservableCollection<ModelCodeDefinition> _modelCodes = new();

    /// <summary>XAML の DataTemplate から参照できる静的な部署リスト</summary>
    public static IReadOnlyList<Department>? DepartmentsSource { get; private set; }

    /// <summary>工程グリッドの表示順を「順序」昇順に固定する</summary>
    private void ApplyProcessGridDefaultSort()
    {
        ProcessGrid.Items.SortDescriptions.Clear();
        ProcessGrid.Items.SortDescriptions.Add(new SortDescription(nameof(ProcessDefinition.SortOrder), ListSortDirection.Ascending));
    }

    public ProcessSettingWindow()
    {
        InitializeComponent();
        ProcessGrid.ItemsSource = _currentDefinitions;
        ApplyProcessGridDefaultSort();
        ModelCodeGrid.ItemsSource = _modelCodes;
        Loaded += async (_, _) =>
        {
            // 部署リストをDBから読み込んでDataGrid.Tag経由でCellEditingTemplateに渡す
            var depts = (await new SqliteDepartmentRepository().GetAllAsync()).ToList();
            // 先頭に「未設定」（Id=0）を追加
            var allDepts = new List<Department> { new Department { Id = 0, Name = "（未設定）" } };
            allDepts.AddRange(depts);
            DepartmentsSource = allDepts;
            ProcessGrid.Tag = allDepts;
            ProcessGrid.IsEnabled = true;

            await RefreshRegisteredListAsync();
            await RefreshModelCodesAsync();
        };
    }

    /// <summary>機種コード登録マスタをDBから読み込む</summary>
    private async Task RefreshModelCodesAsync()
    {
        var modelCodes = await _modelCodeRepository.GetAllAsync();

        // 区分が未設定（空文字列）の既存データは、コンボボックスが空欄表示にならないよう「製品」を初期値とする
        foreach (var m in modelCodes)
            if (string.IsNullOrEmpty(m.Category))
                m.Category = "製品";

        _modelCodes = new ObservableCollection<ModelCodeDefinition>(modelCodes);
        ModelCodeGrid.ItemsSource = _modelCodes;
    }

    /// <summary>「追加」ボタン：機種コード一覧に新しい行を追加する</summary>
    private void BtnAddModelCode_Click(object sender, RoutedEventArgs e)
    {
        _modelCodes.Add(new ModelCodeDefinition { Category = "半製品", SortOrder = _modelCodes.Count });
    }

    /// <summary>機種コード一覧の行削除ボタン：選択行をコレクションから除去する（保存まで確定しない）</summary>
    private void BtnDeleteModelCodeRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ModelCodeDefinition def)
            _modelCodes.Remove(def);
    }

    /// <summary>「保存」ボタン：機種コード一覧をDBの内容と全置換する</summary>
    private async void BtnSaveModelCodes_Click(object sender, RoutedEventArgs e)
    {
        // 機種コードの重複チェック（DBのUNIQUE制約違反を事前に検知し、わかりやすいエラーを表示する）
        var duplicates = _modelCodes
            .Where(m => !string.IsNullOrWhiteSpace(m.ModelCode))
            .GroupBy(m => m.ModelCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            MessageBox.Show($"機種コードが重複しています:\n{string.Join("\n", duplicates)}", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var toSave = _modelCodes.Where(m => !string.IsNullOrWhiteSpace(m.ModelCode)).ToList();
        var savedCount = 0;
        foreach (var def in toSave)
            def.SortOrder = savedCount++;

        await _modelCodeRepository.ReplaceAllAsync(toSave);

        await RefreshModelCodesAsync();
        TxtModelCodeStatus.Text = $"保存しました（{savedCount} 件）";
    }

    /// <summary>DB に登録済みの品目番号一覧を保持する（一覧選択ウィンドウ用）</summary>
    private List<ItemPickerEntry> _registeredItems = new();

    /// <summary>登録済み品目一覧の読み込みタスク（一覧選択ウィンドウを開く前に完了を待機する）</summary>
    private Task? _refreshTask;

    /// <summary>DB に登録済みの品目番号一覧を読み込む</summary>
    private Task RefreshRegisteredListAsync()
    {
        return _refreshTask = RefreshRegisteredListInternalAsync();
    }

    private async Task RefreshRegisteredListInternalAsync()
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
        if (_refreshTask != null)
            await _refreshTask;

        var picker = new ItemNumberPickerWindow(_registeredItems) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedItemNumber is not string itemNumber) return;

        await LoadRegisteredItemAsync(itemNumber);
    }

    /// <summary>
    /// 「未登録品目から選択」ボタン。
    /// 機種コード登録で「半製品」に区分した機種コードにある未登録の品目番号を一覧表示し、選択した品目をPRONESSから取り込んで登録する。
    /// </summary>
    private async void BtnSelectUnregisteredItems_Click(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Load();
        if (!settings.IsOdbcConfigured)
        {
            TxtStatus.Text = "設定からODBC接続情報を入力してください";
            return;
        }

        var semiProductModelCodes = (await _modelCodeRepository.GetAllAsync())
            .Where(m => m.Category == "半製品")
            .Select(m => m.ModelCode)
            .ToList();
        if (!semiProductModelCodes.Any())
        {
            TxtStatus.Text = "機種コード登録に「半製品」が登録されていません";
            return;
        }

        if (_refreshTask != null)
            await _refreshTask;

        LoadingOverlay.Visibility = Visibility.Visible;
        TxtLoadingMessage.Text = "未登録品目を取得中...";
        List<UnregisteredItemEntry> entries;
        try
        {
            var excludeSeibanMatch = ChkExcludeItemNumberEqualsSeiban.IsChecked == true;
            var excludeStartsWithM = ChkExcludeItemNumberStartsWithM.IsChecked == true;
            var orderRepo = new OdbcOrderRepository(settings);
            var allItems = await Task.Run(() => orderRepo.GetSemiFinishedItemNumbersWithNames(semiProductModelCodes, excludeSeibanMatch, excludeStartsWithM).ToList());

            var registeredSet = new HashSet<string>(_registeredItems.Select(i => i.ItemNumber), StringComparer.OrdinalIgnoreCase);

            entries = allItems
                .Where(i => !registeredSet.Contains(i.ItemNumber))
                .Select(i => new UnregisteredItemEntry
                {
                    ItemNumber = i.ItemNumber,
                    DisplayName = i.ItemName
                })
                .ToList();
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            TxtLoadingMessage.Text = "取得中...";
        }

        if (!entries.Any())
        {
            TxtStatus.Text = "未登録の品目番号はありません";
            return;
        }

        var picker = new UnregisteredItemPickerWindow(entries) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedItemNumbers is not List<string> selectedItemNumbers) return;

        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            var odbcRepo = new OdbcProcessDefinitionRepository(settings);
            var registered = 0;
            var skipped = 0;

            TxtLoadingMessage.Text = "取得中...";
            var odbcDefsByItem = await Task.Run(() => odbcRepo.GetByItemNumbers(selectedItemNumbers));

            foreach (var itemNumber in selectedItemNumbers)
            {
                TxtLoadingMessage.Text = $"登録中...（{registered + skipped + 1}/{selectedItemNumbers.Count}）";

                var odbcDefs = odbcDefsByItem[itemNumber];
                if (!odbcDefs.Any())
                {
                    skipped++;
                    continue;
                }

                foreach (var def in odbcDefs)
                {
                    def.ItemNumber = itemNumber;
                    await _dbRepository.AddAsync(def);
                }

                var entry = entries.First(i => i.ItemNumber == itemNumber);
                if (!string.IsNullOrEmpty(entry.DisplayName))
                    await _nameRepository.SaveDisplayNameAsync(itemNumber, entry.DisplayName);

                registered++;
            }

            await RefreshRegisteredListAsync();
            TxtStatus.Text = $"{selectedItemNumbers.Count} 件中 {registered} 件を登録しました（スキップ {skipped} 件）";
        }
        finally
        {
            TxtLoadingMessage.Text = "取得中...";
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
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
        ApplyProcessGridDefaultSort();
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
            var odbcDefs = (await Task.Run(() => odbcRepo.GetByItemNumber(itemNumber))).ToList();

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

            // ODBC構造 + DB設定をマージ
            // 順序・指示先番号はODBCが正、工程名・LT・表示・警告はDB設定を優先
            _currentDefinitions = new ObservableCollection<ProcessDefinition>(MergeOdbcWithDb(itemNumber, odbcDefs, dbDefs));
            ProcessGrid.ItemsSource = _currentDefinitions;
            ApplyProcessGridDefaultSort();
            TxtStatus.Text = $"{odbcDefs.Count} 件の工程を取り込みました";
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>ODBCから取得した工程一覧に、DB既存設定（工程名・LT・表示・警告等）をマージする。
    /// 順序・指示先番号はODBCを正とする。</summary>
    private static List<ProcessDefinition> MergeOdbcWithDb(string itemNumber, List<ProcessDefinition> odbcDefs, List<ProcessDefinition> dbDefs)
    {
        var dbDict = dbDefs
            .Where(d => !string.IsNullOrEmpty(d.DestinationCode))
            .GroupBy(d => d.DestinationCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return odbcDefs.Select(odbcDef =>
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
        }).ToList();
    }

    /// <summary>指定品目番号の工程定義をDB上で全置換する</summary>
    private async Task ReplaceDefinitionsInDbAsync(string itemNumber, List<ProcessDefinition> definitions)
    {
        foreach (var def in definitions)
            def.ItemNumber = itemNumber;

        await _dbRepository.ReplaceForItemNumberAsync(itemNumber, definitions);
    }

    /// <summary>
    /// 「登録済み品目を一括更新」ボタン。
    /// 半製品に登録済みの全品目番号についてPRONESSから工程を再取得し、DB既存設定とマージして保存する。
    /// </summary>
    private async void BtnBulkImport_Click(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Load();
        if (!settings.IsOdbcConfigured)
        {
            TxtStatus.Text = "設定からODBC接続情報を入力してください";
            return;
        }

        if (_refreshTask != null)
            await _refreshTask;

        var itemNumbers = _registeredItems.Select(i => i.ItemNumber).ToList();
        if (!itemNumbers.Any())
        {
            TxtStatus.Text = "登録済みの品目番号がありません";
            return;
        }

        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            var odbcRepo = new OdbcProcessDefinitionRepository(settings);
            var updated = 0;
            var skipped = 0;

            TxtLoadingMessage.Text = "取得中...";
            var odbcDefsByItem = await Task.Run(() => odbcRepo.GetByItemNumbers(itemNumbers));

            foreach (var itemNumber in itemNumbers)
            {
                TxtLoadingMessage.Text = $"更新中...（{updated + skipped + 1}/{itemNumbers.Count}）";

                var odbcDefs = odbcDefsByItem[itemNumber];
                if (!odbcDefs.Any())
                {
                    skipped++;
                    continue;
                }

                var dbDefs = (await _dbRepository.GetByItemNumberAsync(itemNumber)).ToList();
                var merged = MergeOdbcWithDb(itemNumber, odbcDefs, dbDefs);
                await ReplaceDefinitionsInDbAsync(itemNumber, merged);
                updated++;
            }

            // 現在表示中の品目が更新対象に含まれていればグリッドを再読み込み
            var currentItem = TxtItemNumber.Text.Trim();
            if (!string.IsNullOrEmpty(currentItem) && itemNumbers.Contains(currentItem, StringComparer.OrdinalIgnoreCase))
                await LoadRegisteredItemAsync(currentItem);

            TxtStatus.Text = $"{itemNumbers.Count} 件中 {updated} 件を更新しました（スキップ {skipped} 件）";
        }
        finally
        {
            TxtLoadingMessage.Text = "取得中...";
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
            foreach (var def in _currentDefinitions)
                def.ItemNumber = itemNumber;

            await _dbRepository.ReplaceForItemNumberAsync(itemNumber, _currentDefinitions);
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
