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

    private readonly Func<AppSettings, IOdbcOrderRepository> _odbcOrderRepositoryFactory;
    private readonly Func<AppSettings, IOdbcProcessDefinitionRepository> _odbcProcessDefinitionRepositoryFactory;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _filterDebounceTimer;

    /// <summary>表示設定ダイアログのリアルタイムプレビュー先（MainWindowが起動時に自分自身を設定する）</summary>
    public IDisplaySettingsPreviewTarget? PreviewTarget { get; set; }

    /// <summary>工程表示モード変更・表示設定の保存など、DataGridの列再構築がView側で必要になったときに発火する</summary>
    public event EventHandler? GridRebuildRequested;

    // 全件キャッシュ（フィルター用）
    private List<Order> _allOrders = [];
    // ODBC工程定義キャッシュ（工程設定変更後の再構築でODBC再アクセスを避けるため保持）
    private List<ProcessDefinition> _allOdbcDefs = [];
    public IReadOnlyList<ProcessDefinition> OdbcProcessDefinitions => _allOdbcDefs;
    // 製品/半製品区分の判定（フィルター・部署別締切集中度で共有）
    private ProductCategoryClassifier _categoryClassifier = new([], [], []);
    public ProductCategoryClassifier CategoryClassifier => _categoryClassifier;
    // 工程バーの日付バー営業日判定で共有（MainWindow.BuildProcessBarColumn）
    private List<Holiday> _holidays = [];
    public IReadOnlyList<Holiday> Holidays => _holidays;
    // 休日設定の変更を検知するための内容ベースの署名（_holidaysは読み込みのたびに新しいリスト参照になるため、
    // MainWindowの工程列再構築スキップ判定にはリストの参照ではなくこの値を使う）
    public int HolidaysSignature => _holidays.Aggregate(_holidays.Count, (acc, h) => HashCode.Combine(acc, h.Date));
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
        // 「完了」と同時にONだと常に0件になるため、矛盾を避けて解除する
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
        // 「完了以外」と同時にONだと常に0件になるため、矛盾を避けて解除する
        if (value)
            FilterHideCompleted = false;
        ApplyFilter();
    }

    /// <summary>次の未完了工程が着手前の注文のみ表示</summary>
    [ObservableProperty] private bool _filterNotStarted = false;
    partial void OnFilterNotStartedChanged(bool value) => ApplyFilter();

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
        new("出荷日",     SortMode.DeliveryDate),
        new("完了期限日", SortMode.CompletionDate),
        new("工程期限",   SortMode.ProcessDeadline),
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
        await RebuildProcessesAsync();
    }

    // 読み込みが一瞬で終わる環境でもスピナーが視認できるよう最小表示時間を確保する
    private static readonly TimeSpan _minLoadingDisplayDuration = TimeSpan.FromMilliseconds(300);

    // IsLoading中に来たRebuildProcessesAsync要求を、進行中の読み込み完了後にまとめて1回だけ反映するための保留フラグ
    private bool _pendingRebuildRequested;
    private bool _pendingRebuildReloadHolidays;

    /// <summary>工程設定・休日設定・部署設定ウィンドウでのDB変更（工程マスタ・休日・部署等）を画面に反映する。
    /// これらのウィンドウが変更するのはSQLite側のみで受注データ自体はODBCから変わらないため、
    /// ODBCへの再アクセスはせず、キャッシュ済みの_allOrders・_allOdbcDefsに対してBuildのみ再実行する。
    /// 進行中の読み込み（LoadOrdersAsync/RebuildProcessesAsync）と重なった場合は、その完了を待って
    /// 保留リクエストとして1回だけ再実行する（無視すると設定変更が反映されないまま終わってしまうため）。
    /// 初回のLoadOrdersAsyncが未完了（ODBCキャッシュが無い）場合は通常のLoadOrdersAsyncにフォールバックする</summary>
    private async Task RebuildProcessesAsync(bool reloadHolidays = false) {
        if (IsLoading) {
            _pendingRebuildRequested = true;
            _pendingRebuildReloadHolidays |= reloadHolidays;
            StatusMessage = "読み込み中のため、変更内容は現在の読み込み完了後に反映されます。";
            return;
        }

        if (_lastLoaded is null) {
            await LoadOrdersAsync();
            return;
        }

        IsLoading = true;
        var startedAt = DateTime.UtcNow;
        try {
            if (reloadHolidays) {
                var holidays = await _holidayRepository.GetAllAsync();
                _holidays = holidays.ToList();
            }

            var calculator = new BusinessDayCalculator(_holidays, Settings.DayMinutes);
            var today = DateOnly.FromDateTime(DateTime.Today);

            var displayNames = await Repositories.SqliteProductDisplayNameRepository.GetAllDisplayNamesAsync();
            var leadDaysOverrides = await Repositories.SqliteProductDisplayNameRepository.GetAllCompletionDateLeadDaysAsync();
            var dbDefs = await _processDefinitionRepository.GetAllAsync();

            OrderProcessBuildService.Build(_allOrders, _allOdbcDefs, dbDefs.ToList(), displayNames, leadDaysOverrides, Settings.CompletionDateLeadDays, calculator, today);

            var modelCodes = await _modelCodeRepository.GetAllAsync();
            var registeredNumbers = await _processDefinitionRepository.GetItemNumbersAsync();
            _categoryClassifier = new ProductCategoryClassifier(
                modelCodes.Where(m => m.Category == "製品").Select(m => m.ModelCode),
                modelCodes.Where(m => m.Category == "半製品").Select(m => m.ModelCode),
                registeredNumbers);

            ApplyFilter();

            var elapsed = DateTime.UtcNow - startedAt;
            if (elapsed < _minLoadingDisplayDuration)
                await Task.Delay(_minLoadingDisplayDuration - elapsed);
        } catch (Exception ex) {
            StatusMessage = $"読み込みエラー：{ex.Message}";
            System.Windows.MessageBox.Show(ex.Message, "読み込みエラー",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        } finally {
            IsLoading = false;
            await RunPendingRebuildAsync();
        }
    }

    /// <summary>IsLoading中に来たRebuildProcessesAsync要求を、読み込み完了直後に1回だけ反映する</summary>
    private async Task RunPendingRebuildAsync() {
        if (!_pendingRebuildRequested) return;
        _pendingRebuildRequested = false;
        var reloadHolidays = _pendingRebuildReloadHolidays;
        _pendingRebuildReloadHolidays = false;
        await RebuildProcessesAsync(reloadHolidays);
    }

    [RelayCommand]
    private async Task OpenHolidaySettingAsync() {
        _dialogService.ShowHolidaySetting();
        await RebuildProcessesAsync(reloadHolidays: true);
    }

    [RelayCommand]
    private async Task OpenDepartmentSettingAsync() {
        _dialogService.ShowDepartmentSetting();
        // 部署削除でProcessDefinitions.DepartmentIdが0にリセットされている可能性があるため、Processesを再構築
        await RebuildProcessesAsync();
        // 部署マスタが変更された可能性があるため、フィルターボタンリストを更新
        await RefreshDepartmentFiltersAsync();
    }

    [RelayCommand]
    private void OpenDepartmentAbsenceSetting() => _dialogService.ShowDepartmentAbsenceSetting();

    [RelayCommand]
    private void OpenProductPerformance() => _dialogService.ShowProductPerformance(Settings);

    [RelayCommand]
    private void OpenDepartmentLoad() => _dialogService.ShowDepartmentLoad(_allOrders, _categoryClassifier, Settings);

    [RelayCommand]
    private void OpenDashboard() => _dialogService.ShowDashboard(_allOrders, _categoryClassifier, Settings, Holidays, OdbcProcessDefinitions, _odbcOrderRepositoryFactory);

    [RelayCommand]
    private void OpenProcessBottleneck() => _dialogService.ShowProcessBottleneck(Settings);

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

    [RelayCommand] private void ClearItemNumberFilter() => FilterItemNumber = string.Empty;
    [RelayCommand] private void ClearProductNameFilter() => FilterProductName = string.Empty;
    [RelayCommand] private void ClearManufactureNumberFilter() => FilterManufactureNumber = string.Empty;
    [RelayCommand]
    private void ClearDeliveryDateRangeFilter() {
        FilterDeliveryFrom = null;
        FilterDeliveryTo = null;
    }

    private OrderFilterCriteria BuildFilterCriteria() => new() {
        ItemNumber = FilterItemNumber,
        ProductName = FilterProductName,
        ManufactureNumber = FilterManufactureNumber,
        DeliveryFrom = FilterDeliveryFrom,
        DeliveryTo = FilterDeliveryTo,
        HideCompleted = FilterHideCompleted,
        OverdueOnly = FilterOverdueOnly,
        WarningOnly = FilterWarningOnly,
        TodayTaskOnly = FilterTodayTask,
        CompletedOnly = FilterCompletedOnly,
        NotStartedOnly = FilterNotStarted,
        ProductCategory = FilterProductCategory,
        DepartmentId = FilterDepartmentId,
    };

    public void ApplyFilter() {
        var filtered = OrderFilterService.Apply(_allOrders, BuildFilterCriteria(), _categoryClassifier, Settings.SortMode, Settings.ShowDueDateForNotStarted);
        Orders = new ObservableCollection<Order>(filtered);
        UpdateStatusMessage();
    }

    private bool _isUpdatingFilters;

    /// <summary>部署マスタを再取得してフィルターコンボボックスの選択肢を更新する</summary>
    public async Task RefreshDepartmentFiltersAsync() {
        var departments = await Repositories.SqliteDepartmentRepository.GetAllAsync();
        var previousSelectedId = FilterDepartmentId;
        _isUpdatingFilters = true;
        try {
            DepartmentFilters.Clear();
            DepartmentFilters.Add(new DepartmentFilterItem { Id = 0, Name = "全て" });
            foreach (var d in departments)
                DepartmentFilters.Add(new DepartmentFilterItem { Id = d.Id, Name = d.Name });
            FilterDepartmentId = departments.Any(d => d.Id == previousSelectedId) ? previousSelectedId : 0;
            // DepartmentFilters.Clear()でコンボボックスが選択状態を失うため、
            // 値が変化しない場合でも表示を再同期させるために明示的に通知する
            OnPropertyChanged(nameof(FilterDepartmentId));
        } finally {
            _isUpdatingFilters = false;
        }
        ApplyFilter();
    }

    private void RefreshFilterDateRange() {
        FilterDateMin = DateTime.Today.AddDays(-Settings.DeliveryDatePastDays);
        FilterDateMax = DateTime.Today.AddDays(Settings.DeliveryDateRangeDays);
    }

    private void UpdateStatusMessage() {
        // 見落とし防止のため、現在の絞り込みに関わらず全件（_allOrders）から集計する
        var overdueCount = _allOrders.Count(o => o.HasOverdue);
        var warningCount = _allOrders.Count(o => o.HasWarning);
        var alertStr = overdueCount > 0 || warningCount > 0
            ? $"　超過：{overdueCount}件　警告：{warningCount}件"
            : string.Empty;
        var lastStr = _lastLoaded.HasValue ? $"　最終更新：{_lastLoaded.Value:HH:mm:ss}" : string.Empty;
        StatusMessage = $"{Orders.Count} 件表示中（全 {_allOrders.Count} 件）{alertStr}{lastStr}";
    }

    [ObservableProperty]
    private string _statusMessage = "データを読み込んでいます...";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private AppSettings _settings;

    private bool _holidaysSynced;

    public MainViewModel(IHolidayRepository holidayRepository, IProcessDefinitionRepository processDefinitionRepository, IModelCodeRepository modelCodeRepository, IDialogService dialogService, Func<AppSettings, IOdbcOrderRepository> odbcOrderRepositoryFactory, Func<AppSettings, IOdbcProcessDefinitionRepository> odbcProcessDefinitionRepositoryFactory) {
        _holidayRepository = holidayRepository;
        _processDefinitionRepository = processDefinitionRepository;
        _modelCodeRepository = modelCodeRepository;
        _dialogService = dialogService;
        _odbcOrderRepositoryFactory = odbcOrderRepositoryFactory;
        _odbcProcessDefinitionRepositoryFactory = odbcProcessDefinitionRepositoryFactory;

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
        var startedAt = DateTime.UtcNow;

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
                var hasAnyRecord = await Task.Run(() => _odbcOrderRepositoryFactory(settings).HasAnySeisanKeikakuRecord());
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

            var holidaySyncFailed = false;
            if (!_holidaysSynced) {
                // 同期成功時のみtrueにする。失敗時はfalseのままにして、次回のLoadOrdersAsync呼び出し
                // （自動更新タイマー等）で再試行できるようにする
                _holidaysSynced = await SyncHolidaysFromOdbcAsync(settings);
                holidaySyncFailed = !_holidaysSynced;
            }

            var holidays = await _holidayRepository.GetAllAsync();
            _holidays = holidays.ToList();
            var calculator = new BusinessDayCalculator(holidays, Settings.DayMinutes);
            var today = DateOnly.FromDateTime(DateTime.Today);

            // DB登録済みの品目名・完了日リードタイムのユーザー設定（未設定の品目はリードタイムに含まれないため、参照時に共通設定へフォールバックする）
            var displayNames = await Repositories.SqliteProductDisplayNameRepository.GetAllDisplayNamesAsync();
            var leadDaysOverrides = await Repositories.SqliteProductDisplayNameRepository.GetAllCompletionDateLeadDaysAsync();
            var dbDefs = await _processDefinitionRepository.GetAllAsync();

            OrderProcessBuildService.Build(orders, allOdbcDefs, dbDefs.ToList(), displayNames, leadDaysOverrides, Settings.CompletionDateLeadDays, calculator, today);

            _allOrders = orders.OrderBy(o => o.DeliveryDate).ToList();
            _allOdbcDefs = allOdbcDefs;

            var modelCodes = await _modelCodeRepository.GetAllAsync();
            var registeredNumbers = await _processDefinitionRepository.GetItemNumbersAsync();
            _categoryClassifier = new ProductCategoryClassifier(
                modelCodes.Where(m => m.Category == "製品").Select(m => m.ModelCode),
                modelCodes.Where(m => m.Category == "半製品").Select(m => m.ModelCode),
                registeredNumbers);

            // 部署フィルターボタンリストを更新
            await RefreshDepartmentFiltersAsync();

            _lastLoaded = DateTime.Now;
            ApplyFilter();
            if (holidaySyncFailed)
                StatusMessage += "（休日データの自動同期に失敗したため、既存の休日データで計算しています）";

            // ODBCが高速に応答する環境でもスピナーが視認できるよう最小表示時間を確保する
            // （通常はODBC通信自体が300ms以上かかるため、ここでの待機はほぼ発生しない）
            var elapsed = DateTime.UtcNow - startedAt;
            if (elapsed < _minLoadingDisplayDuration)
                await Task.Delay(_minLoadingDisplayDuration - elapsed);

        } catch (Exception ex) {
            StatusMessage = $"読み込みエラー：{ex.Message}";
            System.Windows.MessageBox.Show(ex.Message, "読み込みエラー",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        } finally {
            IsLoading = false;
            await RunPendingRebuildAsync();
        }
    }

    /// <summary>ODBC（ERP）から注文と工程定義を取得する。同期処理のためTask.Runでスレッドプールに逃がす</summary>
    private async Task<(List<Order> Orders, List<ProcessDefinition> Defs)> FetchOdbcDataAsync(AppSettings settings) {
        return await Task.Run(() => {
            var repo = _odbcOrderRepositoryFactory(settings);
            var orders = repo.GetAll().ToList();

            var processRepo = _odbcProcessDefinitionRepositoryFactory(settings);
            var defs = processRepo.GetAll().ToList();
            return (orders, defs);
        });
    }

    /// <summary>起動時に一度だけ、当年・翌年の休日をVP_カレンダ情報_YDから取得しHolidaysへ反映する。
    /// ODBC未設定・接続失敗時は既存のHolidaysデータのまま続行する。戻り値は同期成功可否</summary>
    private async Task<bool> SyncHolidaysFromOdbcAsync(AppSettings settings) {
        // 工場番号が未設定の間は「まだ準備が整っていない」状態として扱い、
        // 設定後の次回LoadOrdersAsync呼び出しで再試行できるようにする
        if (string.IsNullOrEmpty(settings.OdbcFactoryNumber)) return false;

        try {
            await OdbcCalendarRepository.SyncCurrentAndNextYearAsync(settings, _holidayRepository);
            return true;
        } catch {
            // 休日データの同期に失敗しても、既存のHolidaysデータで計算を続行する
            return false;
        }
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
