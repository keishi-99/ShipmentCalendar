using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace ShipmentCalendar.ViewModels;

public partial class MainViewModel : ObservableObject {
    private readonly IHolidayRepository _holidayRepository;
    private readonly AppSettingsService _settingsService;
    private readonly DispatcherTimer _refreshTimer;

    // 全件キャッシュ（フィルター用）
    private List<Order> _allOrders = new();
    // 機種コード登録マスタで「製品」に区分された機種コード一覧（フィルター用）
    private HashSet<string> _productModelCodes = new(StringComparer.OrdinalIgnoreCase);
    // 機種コード登録マスタで「半製品」に区分された機種コード一覧（フィルター用）
    private HashSet<string> _semiProductModelCodes = new(StringComparer.OrdinalIgnoreCase);
    // 工程設定（ProcessDefinitions）に登録済みの品目番号セット（フィルター用）
    private HashSet<string> _registeredItemNumbers = new(StringComparer.OrdinalIgnoreCase);
    // 最終更新日時
    private DateTime? _lastLoaded;

    [ObservableProperty]
    private ObservableCollection<Order> _orders = new();

    [ObservableProperty]
    private Order? _selectedOrder;

    [ObservableProperty] private DateTime _filterDateMin = DateTime.Today;
    [ObservableProperty] private DateTime _filterDateMax = DateTime.Today.AddDays(90);

    [ObservableProperty] private string _filterItemNumber = string.Empty;
    [ObservableProperty] private string _filterProductName = string.Empty;
    [ObservableProperty] private string _filterManufactureNumber = string.Empty;
    [ObservableProperty] private DateTime? _filterDeliveryFrom;
    [ObservableProperty] private DateTime? _filterDeliveryTo;
    [ObservableProperty] private bool _filterHideCompleted;

    /// <summary>製品/半製品フィルター: "全て" / "半製品" / "製品"</summary>
    [ObservableProperty] private string _filterProductCategory = "全て";

    public static IReadOnlyList<string> ProductCategoryOptions { get; } =
        new[] { "全て", "製品", "半製品", "半製品（工程未登録）", "どちらでもない" };

    /// <summary>ツールバー部署フィルターボタン用リスト（「全て」含む）</summary>
    public ObservableCollection<DepartmentFilterItem> DepartmentFilters { get; } = new();

    /// <summary>選択中の担当部署ID（0=全て）</summary>
    [ObservableProperty] private int _filterDepartmentId = 0;

    partial void OnFilterItemNumberChanged(string value) => ApplyFilter();
    partial void OnFilterProductNameChanged(string value) => ApplyFilter();
    partial void OnFilterManufactureNumberChanged(string value) => ApplyFilter();
    partial void OnFilterDeliveryFromChanged(DateTime? value) => ApplyFilter();
    partial void OnFilterDeliveryToChanged(DateTime? value) => ApplyFilter();
    partial void OnFilterHideCompletedChanged(bool value) => ApplyFilter();
    partial void OnFilterProductCategoryChanged(string value) => ApplyFilter();
    partial void OnFilterDepartmentIdChanged(int value)
    {
        // 各ボタンの選択状態を同期してからフィルターを適用
        foreach (var item in DepartmentFilters)
            item.IsSelected = item.Id == value;
        ApplyFilter();
    }

    [RelayCommand]
    private void SelectDepartment(int id) => FilterDepartmentId = id;

    public void ClearFilter() {
        FilterItemNumber = string.Empty;
        FilterProductName = string.Empty;
        FilterManufactureNumber = string.Empty;
        FilterDeliveryFrom = null;
        FilterDeliveryTo = null;
        FilterProductCategory = "全て";
    }

    /// <summary>注文の「次の未完了工程」の必須日（表示設定に応じてDueDate/StartDate）を返す。
    /// 全工程完了済みならDateOnly.MaxValue</summary>
    private DateOnly GetNextProcessSortDate(Order o) {
        var next = o.Processes
            .Where(p => p.Status != ProcessStatus.Completed)
            .OrderBy(p => p.SortOrder)
            .FirstOrDefault();
        if (next == null) return DateOnly.MaxValue;
        return Settings.ShowDueDateForNotStarted ? next.DueDate : next.StartDate;
    }

    public void ApplyFilter() {
        var result = _allOrders.AsEnumerable();

        if (!string.IsNullOrEmpty(FilterItemNumber))
            result = result.Where(o => o.ItemNumber.Contains(FilterItemNumber, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(FilterProductName))
            result = result.Where(o => o.ProductName.Contains(FilterProductName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(FilterManufactureNumber))
            result = result.Where(o => o.ManufactureNumber.Contains(FilterManufactureNumber, StringComparison.OrdinalIgnoreCase));

        if (FilterDeliveryFrom.HasValue)
            result = result.Where(o => o.DeliveryDate >= DateOnly.FromDateTime(FilterDeliveryFrom.Value));

        if (FilterDeliveryTo.HasValue)
            result = result.Where(o => o.DeliveryDate <= DateOnly.FromDateTime(FilterDeliveryTo.Value));

        if (FilterHideCompleted)
            result = result.Where(o => !o.Processes.Any() || o.Processes.OrderBy(p => p.SortOrder).Last().Status != ProcessStatus.Completed);

        // 製品/半製品/どちらでもないフィルター（機種コード登録マスタの区分で判定）
        if (FilterProductCategory == "製品")
            result = result.Where(o => _productModelCodes.Contains(o.ModelCode));
        else if (FilterProductCategory == "半製品")
            result = result.Where(o => _semiProductModelCodes.Contains(o.ModelCode));
        else if (FilterProductCategory == "半製品（工程未登録）")
            result = result.Where(o => _semiProductModelCodes.Contains(o.ModelCode) && !_registeredItemNumbers.Contains(o.ItemNumber));
        else if (FilterProductCategory == "どちらでもない")
            result = result.Where(o => !_productModelCodes.Contains(o.ModelCode) && !_semiProductModelCodes.Contains(o.ModelCode));

        // 担当部署フィルター：未完了工程のうち SortOrder 最小のものが選択部署の行のみ表示
        if (FilterDepartmentId > 0)
        {
            result = result.Where(o => {
                var next = o.Processes
                    .Where(p => p.Status != ProcessStatus.Completed)
                    .OrderBy(p => p.SortOrder)
                    .FirstOrDefault();
                return next?.DepartmentId == FilterDepartmentId;
            });
        }

        Orders = new ObservableCollection<Order>(Settings.SortByProcessDeadline
            ? result.OrderBy(GetNextProcessSortDate)
            : result.OrderBy(o => o.DeliveryDate));
        UpdateStatusMessage();
    }

    /// <summary>部署マスタを再取得してフィルターボタンリストを更新する</summary>
    public async Task RefreshDepartmentFiltersAsync()
    {
        var departments = await new Repositories.SqliteDepartmentRepository().GetAllAsync();
        DepartmentFilters.Clear();
        DepartmentFilters.Add(new DepartmentFilterItem { Id = 0, Name = "全て", IsSelected = FilterDepartmentId == 0 });
        foreach (var d in departments)
            DepartmentFilters.Add(new DepartmentFilterItem { Id = d.Id, Name = d.Name, IsSelected = FilterDepartmentId == d.Id });
    }

    private void RefreshFilterDateRange()
    {
        FilterDateMin = DateTime.Today.AddDays(-Settings.DeliveryDatePastDays);
        FilterDateMax = DateTime.Today.AddDays(Settings.DeliveryDateRangeDays);
    }

    private void UpdateStatusMessage() {
        var lastStr = _lastLoaded.HasValue ? $"　最終更新：{_lastLoaded.Value:HH:mm:ss}" : string.Empty;
        StatusMessage = $"{Orders.Count} 件表示中（全 {_allOrders.Count} 件）{lastStr}";
    }

    [ObservableProperty]
    private string _statusMessage = "データを読み込んでいます...";

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private AppSettings _settings;

    public MainViewModel(
        IHolidayRepository holidayRepository,
        AppSettingsService settingsService) {
        _holidayRepository = holidayRepository;
        _settingsService = settingsService;
        _settings = settingsService.Load();

        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Tick += async (_, _) => await LoadOrdersAsync();
        ApplyRefreshInterval();
        RefreshFilterDateRange();
    }

    /// <summary>タイマー間隔を設定から再適用する</summary>
    public void ApplyRefreshInterval() {
        _refreshTimer.Stop();
        if (Settings.AutoRefreshMinutes > 0) {
            _refreshTimer.Interval = TimeSpan.FromMinutes(Settings.AutoRefreshMinutes);
            _refreshTimer.Start();
        }
    }

    [RelayCommand]
    public async Task LoadOrdersAsync() {
        if (!Settings.IsOdbcConfigured) {
            StatusMessage = "ODBC接続が設定されていません。設定 > データソース設定 から接続情報を入力してください。";
            return;
        }

        StatusMessage = "読み込み中...";
        IsLoading = true;

        try {
            // ODBC呼び出しは同期処理のためTask.Runでスレッドプールに逃がす
            var settings = Settings;
            var (orders, allOdbcDefs) = await Task.Run(() =>
            {
                var repo = new OdbcOrderRepository(settings);
                var o = repo.GetAll().ToList();

                var processRepo = new OdbcProcessDefinitionRepository(settings);
                var defs = processRepo.GetAll().ToList();
                return (o, defs);
            });

            var holidays = await _holidayRepository.GetAllAsync();
            var calculator = new BusinessDayCalculator(holidays);
            var today = DateOnly.FromDateTime(DateTime.Today);

            // DB登録済みの品目名があればODBC品目名を上書きする
            var displayNames = await new Repositories.SqliteProductDisplayNameRepository().GetAllDisplayNamesAsync();
            foreach (var order in orders) {
                if (displayNames.TryGetValue(order.ItemNumber, out var displayName) && !string.IsNullOrEmpty(displayName))
                    order.ProductName = displayName;
            }

            // DB のユーザー設定（工程名カスタマイズ・LT・表示・警告）をマージ
            // キー: "ItemNumber|DestinationCode(=指示先番号)"
            var dbDefs = await new SqliteProcessDefinitionRepository().GetAllAsync();
            var dbDict = dbDefs
                .Where(d => !string.IsNullOrEmpty(d.DestinationCode))
                .GroupBy(d => $"{d.ItemNumber}|{d.DestinationCode}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var allDefs = allOdbcDefs.Select(odbcDef => {
                var key = $"{odbcDef.ItemNumber}|{odbcDef.DestinationCode}";
                if (!dbDict.TryGetValue(key, out var db)) return odbcDef;
                return new ProcessDefinition {
                    ItemNumber = odbcDef.ItemNumber,
                    ProcessName = db.ProcessName,
                    DestinationCode = odbcDef.DestinationCode,
                    SortOrder = odbcDef.SortOrder,                           // 順序は常にODBC
                    LeadTimeMinutes = db.LeadTimeMinutes ?? odbcDef.LeadTimeMinutes,
                    IsVisible = db.IsVisible,
                    WarningDaysBeforeDeadline = db.WarningDaysBeforeDeadline,
                    DepartmentId = db.DepartmentId,
                    CoolTimeMinutes = db.CoolTimeMinutes,
                    OutsourceLeadDays = db.OutsourceLeadDays
                };
            }).ToList();

            // 品目番号をキーにした工程グループを構築
            var defGroups = allDefs
                .GroupBy(d => d.ItemNumber)
                .Select(g => new { ItemNumber = g.Key, Defs = g.ToList() })
                .ToList();

            foreach (var order in orders) {
                // 品目番号の完全一致で工程定義を取得
                var productDefs = defGroups
                    .Where(g => order.ItemNumber.Equals(g.ItemNumber, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(g => g.Defs)
                    .ToList();

                order.CompletionDate = calculator.SubtractBusinessDays(order.DeliveryDate, Settings.CompletionDateLeadDays);

                if (!productDefs.Any())
                    continue;

                // 仮登録した完了済み指示先番号→受入日のマッピング（指示先番号は工程ごとに一意。重複は先着優先）
                var completedByDestNumber = order.Processes
                    .Where(p => p.Status == ProcessStatus.Completed)
                    .GroupBy(p => p.DestinationCode)
                    .ToDictionary(g => g.Key, g => g.First().ActualDate);

                order.Processes = calculator.BuildProcesses(order, productDefs.Where(d => d.IsVisible), completedByDestNumber);

                // 順序999が完了している場合、前工程すべてを完了扱いにする
                var proc999 = order.Processes.FirstOrDefault(p => p.SortOrder == 999);
                if (proc999?.Status == ProcessStatus.Completed)
                {
                    foreach (var process in order.Processes)
                        process.Status = ProcessStatus.Completed;
                }

                // ステータスを警告日数込みで確定
                foreach (var process in order.Processes) {
                    var warningDays = productDefs
                        .FirstOrDefault(d => d.DestinationCode == process.DestinationCode)
                        ?.WarningDaysBeforeDeadline ?? 0;
                    process.Status = calculator.DetermineStatus(process, today, warningDays);
                }

                // Overdue を後続工程に伝播（完了済みは除く）
                bool overdueFound = false;
                foreach (var process in order.Processes.OrderBy(p => p.SortOrder)) {
                    if (process.Status == ProcessStatus.Overdue)
                        overdueFound = true;
                    else if (overdueFound && process.Status != ProcessStatus.Completed)
                        process.Status = ProcessStatus.Overdue;
                }
            }

            _allOrders = orders.OrderBy(o => o.DeliveryDate).ToList();

            var modelCodes = await new SqliteModelCodeRepository().GetAllAsync();
            _productModelCodes = new HashSet<string>(
                modelCodes.Where(m => m.Category == "製品").Select(m => m.ModelCode), StringComparer.OrdinalIgnoreCase);
            _semiProductModelCodes = new HashSet<string>(
                modelCodes.Where(m => m.Category == "半製品").Select(m => m.ModelCode), StringComparer.OrdinalIgnoreCase);

            var registeredNumbers = await new SqliteProcessDefinitionRepository().GetItemNumbersAsync();
            _registeredItemNumbers = new HashSet<string>(registeredNumbers, StringComparer.OrdinalIgnoreCase);

            // 部署フィルターボタンリストを更新
            await RefreshDepartmentFiltersAsync();

            _lastLoaded = DateTime.Now;
            ApplyFilter();

        } catch (Exception ex) {
            StatusMessage = $"読み込みエラー：{ex.Message}";
            System.Windows.MessageBox.Show(ex.Message, "読み込みエラー",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        } finally {
            IsLoading = false;
        }
    }

    /// <summary>注文一覧の並び順（出荷日順/工程期限順）を切り替える</summary>
    public void ToggleSortMode() {
        Settings.SortByProcessDeadline = !Settings.SortByProcessDeadline;
        SaveSettings();
        ApplyFilter();
    }

    public void SaveSettings() {
        _settingsService.Save(Settings);
        ApplyRefreshInterval();

        RefreshFilterDateRange();

        // 選択中の日付が新しい範囲外なら範囲内にクランプする
        if (FilterDeliveryFrom.HasValue && FilterDeliveryFrom.Value < FilterDateMin)
            FilterDeliveryFrom = FilterDateMin;
        if (FilterDeliveryFrom.HasValue && FilterDeliveryFrom.Value > FilterDateMax)
            FilterDeliveryFrom = null;

        if (FilterDeliveryTo.HasValue && FilterDeliveryTo.Value > FilterDateMax)
            FilterDeliveryTo = FilterDateMax;
        if (FilterDeliveryTo.HasValue && FilterDeliveryTo.Value < FilterDateMin)
            FilterDeliveryTo = null;
    }
}
