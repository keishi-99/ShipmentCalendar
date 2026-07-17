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
    private readonly IProcessDefinitionRepository _processDefinitionRepository;
    private readonly IModelCodeRepository _modelCodeRepository;
    private readonly IDialogService _dialogService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _filterDebounceTimer;

    /// <summary>表示設定ダイアログのリアルタイムプレビュー先（MainWindowが起動時に自分自身を設定する）</summary>
    public IDisplaySettingsPreviewTarget? PreviewTarget { get; set; }

    /// <summary>工程表示モード変更・表示設定の保存など、DataGridの列再構築がView側で必要になったときに発火する</summary>
    public event EventHandler? GridRebuildRequested;

    // 全件キャッシュ（フィルター用）
    private List<Order> _allOrders = [];
    // 機種コード登録マスタで「製品」に区分された機種コード一覧（フィルター用）
    private HashSet<string> _productModelCodes = new(StringComparer.OrdinalIgnoreCase);
    // 機種コード登録マスタで「半製品」に区分された機種コード一覧（フィルター用）
    private HashSet<string> _semiProductModelCodes = new(StringComparer.OrdinalIgnoreCase);
    // 工程設定（ProcessDefinitions）に登録済みの品目番号セット（フィルター用）
    private HashSet<string> _registeredItemNumbers = new(StringComparer.OrdinalIgnoreCase);
    // 最終更新日時
    private DateTime? _lastLoaded;

    [ObservableProperty]
    private ObservableCollection<Order> _orders = [];

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
    [ObservableProperty] private bool _filterTodayOnly;

    /// <summary>製品/半製品フィルター: "全て" / "半製品" / "製品"</summary>
    [ObservableProperty] private string _filterProductCategory = "全て";

    public static IReadOnlyList<string> ProductCategoryOptions { get; } =
        ["全て", "製品", "半製品", "半製品（工程未登録）", "どちらでもない"];

    /// <summary>ツールバー部署フィルターコンボボックス用リスト（「全て」含む）</summary>
    public ObservableCollection<DepartmentFilterItem> DepartmentFilters { get; } = [];

    /// <summary>選択中の担当部署ID（0=全て）</summary>
    [ObservableProperty] private int _filterDepartmentId = 0;

    partial void OnFilterItemNumberChanged(string value) => ScheduleFilter();
    partial void OnFilterProductNameChanged(string value) => ScheduleFilter();
    partial void OnFilterManufactureNumberChanged(string value) => ScheduleFilter();

    private void ScheduleFilter() {
        _filterDebounceTimer.Stop();
        _filterDebounceTimer.Start();
    }
    partial void OnFilterDeliveryFromChanged(DateTime? value) => ApplyFilter();
    partial void OnFilterDeliveryToChanged(DateTime? value) => ApplyFilter();
    partial void OnFilterHideCompletedChanged(bool value) {
        // 「完了のみ表示」と同時にONだと常に0件になるため、矛盾を避けて解除する
        if (value)
            FilterCompletedOnly = false;
        ApplyFilter();
    }

    /// <summary>超過工程がある注文のみ表示</summary>
    [ObservableProperty] private bool _filterOverdueOnly = false;
    partial void OnFilterOverdueOnlyChanged(bool value) => ApplyFilter();

    /// <summary>警告工程がある注文のみ表示</summary>
    [ObservableProperty] private bool _filterWarningOnly = false;
    partial void OnFilterWarningOnlyChanged(bool value) => ApplyFilter();

    /// <summary>次の未完了工程の着手〜完了期間が今日を含む注文のみ表示</summary>
    [ObservableProperty] private bool _filterTodayTask = false;
    partial void OnFilterTodayTaskChanged(bool value) => ApplyFilter();

    /// <summary>全工程が完了済みの注文のみ表示</summary>
    [ObservableProperty] private bool _filterCompletedOnly = false;
    partial void OnFilterCompletedOnlyChanged(bool value) {
        // 「完了済みを非表示」と同時にONだと常に0件になるため、矛盾を避けて解除する
        if (value)
            FilterHideCompleted = false;
        ApplyFilter();
    }

    /// <summary>「本日のみ」トグル：ONなら出荷日範囲を今日に固定し、OFFなら範囲をクリアする</summary>
    partial void OnFilterTodayOnlyChanged(bool value) {
        if (value) {
            FilterDeliveryFrom = DateTime.Today;
            FilterDeliveryTo = DateTime.Today;
        }
        else {
            FilterDeliveryFrom = null;
            FilterDeliveryTo = null;
        }
    }
    partial void OnFilterProductCategoryChanged(string value) => ApplyFilter();
    partial void OnFilterDepartmentIdChanged(int value) {
        if (!_isUpdatingFilters) ApplyFilter();
    }

    /// <summary>並び順コンボボックスの選択肢（ItemsSource用）</summary>
    public ObservableCollection<MenuOption<SortMode>> SortModeItems { get; } = [
        new("出荷日",   SortMode.DeliveryDate),
        new("完了日",   SortMode.CompletionDate),
        new("工程期限", SortMode.ProcessDeadline),
    ];

    public SortMode SelectedSortMode {
        get => Settings.SortMode;
        set {
            if (Settings.SortMode == value) return;
            Settings.SortMode = value;
            OnPropertyChanged();
            SaveSettings();
            ApplyFilter();
        }
    }

    /// <summary>未完了工程の表示日切り替えコンボボックスの選択肢（ItemsSource用）</summary>
    public ObservableCollection<MenuOption<bool>> DueDateDisplayItems { get; } = [
        new("着手期限", false),
        new("完了期限", true),
    ];

    public bool SelectedDueDateDisplay {
        get => Settings.ShowDueDateForNotStarted;
        set {
            if (Settings.ShowDueDateForNotStarted == value) return;
            Settings.ShowDueDateForNotStarted = value;
            OnPropertyChanged();
            SaveSettings();
            ApplyFilter();
        }
    }

    /// <summary>工程表示モードコンボボックスの選択肢（ItemsSource用）</summary>
    public ObservableCollection<MenuOption<ProcessMode>> ProcessModeItems { get; } = [
        new("バー",   ProcessMode.Bar),
        new("リスト", ProcessMode.List),
    ];

    public ProcessMode SelectedProcessMode {
        get => Settings.ShowProcessBar ? ProcessMode.Bar : ProcessMode.List;
        set {
            var showBar = value == ProcessMode.Bar;
            var showColumns = value == ProcessMode.List;
            if (Settings.ShowProcessBar == showBar && Settings.ShowProcessColumns == showColumns) return;
            Settings.ShowProcessBar = showBar;
            Settings.ShowProcessColumns = showColumns;
            OnPropertyChanged();
            SaveSettings();
            GridRebuildRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void OpenBasicSettings() => _dialogService.ShowBasicSettings(this);

    [RelayCommand]
    private async Task OpenProcessSettingAsync() {
        _dialogService.ShowProcessSetting();
        await LoadOrdersAsync();
    }

    [RelayCommand]
    private void OpenHolidaySetting() => _dialogService.ShowHolidaySetting();

    [RelayCommand]
    private async Task OpenDepartmentSettingAsync() {
        _dialogService.ShowDepartmentSetting();
        // 部署マスタが変更された可能性があるため、フィルターボタンリストを更新
        await RefreshDepartmentFiltersAsync();
    }

    [RelayCommand]
    private void OpenProductPerformance() => _dialogService.ShowProductPerformance(Settings);

    [RelayCommand]
    private void OpenDisplaySettings() {
        if (PreviewTarget is null) return;
        _dialogService.ShowDisplaySettings(this, PreviewTarget);
    }

    [RelayCommand]
    public void ClearFilter() {
        FilterItemNumber = string.Empty;
        FilterProductName = string.Empty;
        FilterManufactureNumber = string.Empty;
        FilterTodayOnly = false;
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
            result = result.Where(o => o.Processes.Count == 0 || o.Processes.Any(p => p.Status != ProcessStatus.Completed));

        if (FilterOverdueOnly || FilterWarningOnly || FilterTodayTask || FilterCompletedOnly) {
            var today = DateOnly.FromDateTime(DateTime.Today);
            result = result.Where(o => {
                var isOverdue = FilterOverdueOnly && o.HasOverdue;
                var isWarning = FilterWarningOnly && o.Processes.Any(p => p.Status == ProcessStatus.Warning);
                var isToday = false;
                if (FilterTodayTask) {
                    var next = o.Processes
                        .Where(p => p.Status != ProcessStatus.Completed)
                        .OrderBy(p => p.SortOrder)
                        .FirstOrDefault();
                    isToday = next != null && today >= next.StartDate && today <= next.DueDate;
                }
                var isCompleted = FilterCompletedOnly && o.IsAllCompleted;
                return isOverdue || isWarning || isToday || isCompleted;
            });
        }

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
        if (FilterDepartmentId > 0) {
            result = result.Where(o => {
                var next = o.Processes
                    .Where(p => p.Status != ProcessStatus.Completed)
                    .OrderBy(p => p.SortOrder)
                    .FirstOrDefault();
                return next?.DepartmentId == FilterDepartmentId;
            });
        }

        Orders = new ObservableCollection<Order>(Settings.SortMode switch {
            SortMode.CompletionDate  => result.OrderBy(o => o.CompletionDate),
            SortMode.ProcessDeadline => result.OrderBy(GetNextProcessSortDate),
            _                        => result.OrderBy(o => o.DeliveryDate),
        });
        UpdateStatusMessage();
    }

    private bool _isUpdatingFilters;

    /// <summary>部署マスタを再取得してフィルターコンボボックスの選択肢を更新する</summary>
    public async Task RefreshDepartmentFiltersAsync() {
        var departments = await Repositories.SqliteDepartmentRepository.GetAllAsync();
        var previousSelectedId = FilterDepartmentId;
        _isUpdatingFilters = true;
        DepartmentFilters.Clear();
        DepartmentFilters.Add(new DepartmentFilterItem { Id = 0, Name = "全て" });
        foreach (var d in departments)
            DepartmentFilters.Add(new DepartmentFilterItem { Id = d.Id, Name = d.Name });
        FilterDepartmentId = departments.Any(d => d.Id == previousSelectedId) ? previousSelectedId : 0;
        _isUpdatingFilters = false;
        ApplyFilter();
    }

    private void RefreshFilterDateRange() {
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
    private bool _isLoading;

    [ObservableProperty]
    private AppSettings _settings;

    public MainViewModel(IHolidayRepository holidayRepository, IProcessDefinitionRepository processDefinitionRepository, IModelCodeRepository modelCodeRepository, IDialogService dialogService) {
        _holidayRepository = holidayRepository;
        _processDefinitionRepository = processDefinitionRepository;
        _modelCodeRepository = modelCodeRepository;
        _dialogService = dialogService;
        _settings = AppSettingsService.Load();

        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Tick += async (_, _) => await LoadOrdersAsync();
        ApplyRefreshInterval();
        RefreshFilterDateRange();

        _filterDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _filterDebounceTimer.Tick += (_, _) => { _filterDebounceTimer.Stop(); ApplyFilter(); };
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
            StatusMessage = "ODBC接続が設定されていません。設定 > 基本設定 から接続情報を入力してください。";
            return;
        }

        // 自動更新タイマー等からの呼び出しが、リトライ待機中の呼び出しと多重実行されるのを防ぐ
        if (IsLoading) return;

        StatusMessage = "読み込み中...";
        IsLoading = true;

        const int MaxRetryCount = 3;
        const int RetryIntervalSeconds = 60;

        try {
            var settings = Settings;
            List<Order> orders = [];
            List<ProcessDefinition> allOdbcDefs = [];

            for (var attempt = 0; attempt <= MaxRetryCount; attempt++) {
                (orders, allOdbcDefs) = await FetchOdbcDataAsync(settings);
                if (orders.Count > 0) break;

                // 取得範囲（過去/未来日数設定）の絞り込みによる正常な0件か、
                // ERPの一時的な空読みかを、フィルター無しの存在確認で区別する
                var hasAnyRecord = await Task.Run(() => new OdbcOrderRepository(settings).HasAnySeisanKeikakuRecord());
                if (hasAnyRecord) break;

                if (attempt == MaxRetryCount) {
                    StatusMessage = "受注データが取得できませんでした（0件）。ERPの状態を確認してください。";
                    System.Windows.MessageBox.Show(
                        $"受注データの取得を{MaxRetryCount}回リトライしましたが、すべて0件でした。\nERPの状態を確認し、改めて再読み込みしてください。",
                        "データ取得エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                StatusMessage = $"受注データが0件のため、{RetryIntervalSeconds}秒後にリトライします（{attempt + 1}/{MaxRetryCount}回目）...";
                var popup = new Views.OdbcRetryPopup { Owner = System.Windows.Application.Current.MainWindow };
                var cancelled = await popup.ShowAndCountdownAsync(RetryIntervalSeconds, attempt + 1, MaxRetryCount);
                if (cancelled) {
                    StatusMessage = "読み込みを中止しました。";
                    return;
                }
            }

            var holidays = await _holidayRepository.GetAllAsync();
            var calculator = new BusinessDayCalculator(holidays, Settings.DayMinutes);
            var today = DateOnly.FromDateTime(DateTime.Today);

            // DB登録済みの品目名があればODBC品目名を上書きする
            var displayNames = await Repositories.SqliteProductDisplayNameRepository.GetAllDisplayNamesAsync();
            foreach (var order in orders) {
                if (displayNames.TryGetValue(order.ItemNumber, out var displayName) && !string.IsNullOrEmpty(displayName))
                    order.ProductName = displayName;
            }

            // 品目番号ごとの完了日リードタイム（未設定の品目は含まれないため、参照時に共通設定へフォールバックする）
            var leadDaysOverrides = await Repositories.SqliteProductDisplayNameRepository.GetAllCompletionDateLeadDaysAsync();

            // DB のユーザー設定（工程名カスタマイズ・LT・表示・警告）をマージ
            // キー: "ItemNumber|DestinationCode(=指示先番号)"
            var dbDefs = await _processDefinitionRepository.GetAllAsync();
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
                    SetupTimeMinutes = db.SetupTimeMinutes,
                    WorkTimeMinutes = db.WorkTimeMinutes,
                    IsVisible = db.IsVisible,
                    WarningDaysBeforeDeadline = db.WarningDaysBeforeDeadline,
                    DepartmentId = db.DepartmentId,
                    DwellTimeMinutes = db.DwellTimeMinutes,
                    OutsourceLeadDays = db.OutsourceLeadDays
                };
            }).ToList();

            // 品目番号をキーにした工程定義辞書を構築（O(1)ルックアップ）
            var defDict = allDefs
                .GroupBy(d => d.ItemNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var order in orders) {
                defDict.TryGetValue(order.ItemNumber, out var productDefs);
                productDefs ??= [];

                var leadDays = leadDaysOverrides.TryGetValue(order.ItemNumber, out var itemLeadDays)
                    ? itemLeadDays
                    : Settings.CompletionDateLeadDays;
                order.CompletionDate = calculator.SubtractBusinessDays(order.DeliveryDate, leadDays);

                if (productDefs.Count == 0)
                    continue;

                // 仮登録した完了済み指示先番号→受入日・作業者名のマッピング（指示先番号は工程ごとに一意。重複は先着優先）
                var completedByDestNumber = order.Processes
                    .Where(p => p.Status == ProcessStatus.Completed)
                    .GroupBy(p => p.DestinationCode, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => (g.First().ActualDate, g.First().WorkerName, g.First().ActualWorkMinutes), StringComparer.OrdinalIgnoreCase);

                order.Processes = calculator.BuildProcesses(order, productDefs.Where(d => d.IsVisible), completedByDestNumber);

                // 順序999（最終受入）が完了している場合、前工程すべてを完了扱いにする
                // ※999番工程がIsVisible=falseで非表示でも判定できるよう、フィルタ前のproductDefsと
                //   完了済み指示先番号セットから直接判定する
                var def999 = productDefs.FirstOrDefault(d => d.SortOrder == 999);
                if (def999 != null && completedByDestNumber.ContainsKey(def999.DestinationCode)) {
                    foreach (var process in order.Processes)
                        process.Status = ProcessStatus.Completed;
                }

                // ステータスを警告日数込みで確定
                foreach (var process in order.Processes) {
                    var warningDays = productDefs
                        .FirstOrDefault(d => string.Equals(d.DestinationCode, process.DestinationCode, StringComparison.OrdinalIgnoreCase))
                        ?.WarningDaysBeforeDeadline ?? 0;
                    process.WarningDaysBeforeDeadline = warningDays;
                    process.Status = BusinessDayCalculator.DetermineStatus(process, today, warningDays);
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

            var modelCodes = await _modelCodeRepository.GetAllAsync();
            _productModelCodes = new HashSet<string>(
                modelCodes.Where(m => m.Category == "製品").Select(m => m.ModelCode), StringComparer.OrdinalIgnoreCase);
            _semiProductModelCodes = new HashSet<string>(
                modelCodes.Where(m => m.Category == "半製品").Select(m => m.ModelCode), StringComparer.OrdinalIgnoreCase);

            var registeredNumbers = await _processDefinitionRepository.GetItemNumbersAsync();
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

    /// <summary>ODBC（ERP）から注文と工程定義を取得する。同期処理のためTask.Runでスレッドプールに逃がす</summary>
    private static async Task<(List<Order> Orders, List<ProcessDefinition> Defs)> FetchOdbcDataAsync(AppSettings settings) {
        return await Task.Run(() => {
            var repo = new OdbcOrderRepository(settings);
            var orders = repo.GetAll().ToList();

            var processRepo = new OdbcProcessDefinitionRepository(settings);
            var defs = processRepo.GetAll().ToList();
            return (orders, defs);
        });
    }

    public void SaveSettings() {
        AppSettingsService.Save(Settings);
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
