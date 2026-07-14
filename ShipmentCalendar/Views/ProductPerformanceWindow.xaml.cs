using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class ProductPerformanceWindow : Window {
    private const double LaneBarMaxSize = 200.0;

    private readonly AppSettings _settings;
    private List<ItemPickerEntry> _registeredItems = [];
    private Task? _refreshTask;
    private string? _selectedItemNumber;
    private double _lastScaleMinutes;
    private bool _isLaneView;

    public ProductPerformanceWindow(AppSettings settings) {
        InitializeComponent();
        _settings = settings;
        StartDatePicker.SelectedDate = DateTime.Today.AddDays(-90);
        EndDatePicker.SelectedDate = DateTime.Today;
        Loaded += (_, _) => _refreshTask = RefreshRegisteredItemsAsync();
    }

    private async Task RefreshRegisteredItemsAsync() {
        var itemNumbers = (await new SqliteProcessDefinitionRepository().GetItemNumbersAsync())
            .OrderBy(n => n)
            .ToList();
        var displayNames = await SqliteProductDisplayNameRepository.GetAllDisplayNamesAsync();

        _registeredItems = itemNumbers
            .Select(n => new ItemPickerEntry {
                ItemNumber = n,
                DisplayName = displayNames.TryGetValue(n, out var name) ? name : string.Empty
            })
            .ToList();
    }

    private async void BtnSelectItem_Click(object sender, RoutedEventArgs e) {
        if (_refreshTask != null) {
            try {
                await _refreshTask;
            } catch (Exception ex) {
                MessageBox.Show($"品目リストの読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        var picker = new ItemNumberPickerWindow(_registeredItems) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedItemNumber is not string itemNumber) return;

        _selectedItemNumber = itemNumber;
        var entry = _registeredItems.FirstOrDefault(i => i.ItemNumber == itemNumber);
        TxtSelectedItem.Text = !string.IsNullOrEmpty(entry?.DisplayName)
            ? $"{itemNumber}（{entry.DisplayName}）"
            : itemNumber;
    }

    private async void BtnSearch_Click(object sender, RoutedEventArgs e) {
        if (string.IsNullOrEmpty(_selectedItemNumber)) {
            MessageBox.Show("品目番号を選択してください", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (StartDatePicker.SelectedDate is not DateTime start || EndDatePicker.SelectedDate is not DateTime end || start > end) {
            MessageBox.Show("開始日・終了日を正しく指定してください", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var itemNumber = _selectedItemNumber;
        var from = DateOnly.FromDateTime(start);
        var to = DateOnly.FromDateTime(end);
        var limit = CountCombo.SelectedItem is ComboBoxItem { Tag: string { Length: > 0 } tag } && int.TryParse(tag, out var n)
            ? n
            : (int?)null;

        BtnSearch.IsEnabled = false;
        TxtStatus.Text = "検索中...";
        ResultsControl.ItemsSource = null;

        try {
            var (defs, rows) = await Task.Run(() => {
                var defs = new OdbcProcessDefinitionRepository(_settings).GetByItemNumber(itemNumber).ToList();
                var rows = new OdbcOrderRepository(_settings).GetCompletedProcessesByItemNumberAndDateRange(itemNumber, from, to).ToList();
                return (defs, rows);
            });

            var actualByGroup = BusinessDayCalculator.BuildActualProcesses(
                defs,
                rows.Select(r => (r.Seiban, r.DestinationCode, r.ActualDate, r.WorkerName, r.ActualWorkMinutes)));

            var plannedQuantityBySeiban = rows
                .GroupBy(r => r.Seiban, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().PlannedQuantity, StringComparer.OrdinalIgnoreCase);

            var groups = actualByGroup
                .Select(kv => new ResultGroup(
                    kv.Key,
                    kv.Value.Max(p => p.ActualDate),
                    plannedQuantityBySeiban.GetValueOrDefault(kv.Key, 1),
                    BuildStandardProcesses(defs, plannedQuantityBySeiban.GetValueOrDefault(kv.Key, 1)),
                    kv.Value.OrderBy(p => p.SortOrder).ToList()))
                .OrderByDescending(g => g.LatestActualDate)
                .ToList();

            if (limit.HasValue)
                groups = groups.Take(limit.Value).ToList();

            // 標準・実績バー共通の全幅は、検索結果全体を通した最大値を使うことで「1日」の幅を注文間で揃える
            var maxScaleMinutes = groups.Count == 0 ? 0.0 : RoundToDayBoundary(groups.Max(ComputeRawScaleMinutes));
            groups = groups.Select(g => g with { ScaleMinutes = maxScaleMinutes, Lanes = BuildLanes(g) }).ToList();

            _lastScaleMinutes = maxScaleMinutes;
            RebuildDayRulerHeader(maxScaleMinutes);
            UpdateRulerHeaderVisibility();

            ResultsControl.ItemsSource = groups;
            TxtStatus.Text = groups.Count == 0 ? "該当する実績がありません" : $"{groups.Count} 件表示";
        } catch (Exception ex) {
            TxtStatus.Text = $"検索に失敗しました: {ex.Message}";
        } finally {
            BtnSearch.IsEnabled = true;
        }
    }

    // 標準工数バーは実際の日付を持たないため、休日カレンダー無し・当日基準の仮の注文でBuildProcessesを再利用して組み立てる
    // （バー幅はRequiredMinutesのみで決まるため、日付ラベルに休日が反映されない点を除き表示には影響しない）
    private static List<OrderProcess> BuildStandardProcesses(IEnumerable<ProcessDefinition> defs, int plannedQuantity) {
        var calculator = new BusinessDayCalculator([]);
        var dummyOrder = new Order { CompletionDate = DateOnly.FromDateTime(DateTime.Today), PlannedQuantity = plannedQuantity };
        return calculator.BuildProcesses(dummyOrder, defs, new Dictionary<string, (DateOnly?, string, double)>());
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // 標準工数の合計に1営業日分の余白を足した値（丸め前）
    // 実績が標準を大幅に超過している場合、標準工数だけを基準にするとスケールが崩れて実績バーが
    // 正しく比較できなくなるため、標準・実績のうち大きい方の合計を基準にする
    private static double ComputeRawScaleMinutes(ResultGroup group) {
        var standardMinutes = group.StandardProcesses.Sum(p => p.RequiredMinutes + p.DwellTimeMinutes + p.OutsourceLeadDays * 480.0);
        var actualMinutes = group.ActualProcesses.Sum(p => p.RequiredMinutes + p.DwellTimeMinutes + p.OutsourceLeadDays * 480.0);
        return Math.Max(standardMinutes, actualMinutes) + 480.0;
    }

    // 480分単位に丸める。単純に切り上げるだけだと端数が小さいときに「4日目がほんの少しだけ」のような
    // 見づらい端切れが出るため、480で割った余りが半日（240分）を超える場合のみ次の日に切り上げ、
    // 240分以下なら切り捨てて端切れを消す
    private static double RoundToDayBoundary(double minutes) {
        var remainder = minutes % 480.0;
        return remainder > 240.0 ? minutes + (480.0 - remainder) : minutes - remainder;
    }

    // 工程（指示先番号）ごとに標準・実績の分数をペアにしたレーンを作る。標準は固定長（100%基準）の背景バーとして表現し、
    // 実績バーはその上に重ねて表示する。標準以内の部分と、標準を超えた部分を別の長さとして分けて持たせることで、
    // XAML側で色分けした2色バーとして描ける
    private static List<ProcessLane> BuildLanes(ResultGroup group) {
        var actualByDestination = group.ActualProcesses.ToDictionary(p => p.DestinationCode, StringComparer.OrdinalIgnoreCase);

        return group.StandardProcesses
            .OrderBy(p => p.SortOrder)
            .Select(std => {
                var hasActual = actualByDestination.TryGetValue(std.DestinationCode, out var actual);
                var actualMinutes = hasActual ? actual!.ActualWorkMinutes : 0.0;
                var withinStandardSize = std.RequiredMinutes > 0
                    ? Math.Min(actualMinutes, std.RequiredMinutes) / std.RequiredMinutes * LaneBarMaxSize
                    : 0.0;
                // 標準工数が0分（未設定の工程等）で実績だけがある場合、比率計算では常に0になり実績バーが
                // 見えなくなってしまうため、超過（警告色）として最大幅で表示し実績の存在を示す
                var overflowSize = std.RequiredMinutes > 0
                    ? Math.Max(0, actualMinutes - std.RequiredMinutes) / std.RequiredMinutes * LaneBarMaxSize
                    : actualMinutes > 0 ? LaneBarMaxSize : 0.0;
                return new ProcessLane(
                    std.ProcessName,
                    std.RequiredMinutes,
                    actualMinutes,
                    withinStandardSize,
                    overflowSize,
                    hasActual ? actual!.WorkerName : "");
            })
            .ToList();
    }

    // タイムライン表示専用の共通日数ルーラーを1回だけ組み立てる（全注文がScaleMinutesを共有しているため）。
    // ProcessBarControlの相対日数ルーラーと同じ考え方（480分＝1営業日ごとに区切り、交互背景で「n日目」を表示）
    private void RebuildDayRulerHeader(double totalMinutes) {
        DayRulerGrid.ColumnDefinitions.Clear();
        DayRulerGrid.Children.Clear();
        if (totalMinutes <= 0) return;

        var brushA = (Brush)Application.Current.Resources["DateBarBackgroundBrushA"];
        var brushB = (Brush)Application.Current.Resources["DateBarBackgroundBrushB"];
        var borderBrush = new SolidColorBrush(Color.FromRgb(154, 176, 204));

        var dayCount = Math.Max(1, (int)Math.Ceiling(totalMinutes / 480.0));
        var remaining = totalMinutes;
        for (int day = 0; day < dayCount && remaining >= 1; day++) {
            var width = Math.Min(480.0, remaining);
            DayRulerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
            var border = new Border {
                Background = day % 2 == 0 ? brushA : brushB,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Child = new TextBlock {
                    Text = $"{day + 1}日目",
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.DimGray,
                }
            };
            Grid.SetColumn(border, day);
            DayRulerGrid.Children.Add(border);
            remaining -= width;
        }
    }

    // 共通ルーラーはタイムライン表示のときだけ、かつ検索結果がある場合だけ表示する
    private void UpdateRulerHeaderVisibility() =>
        RulerHeaderBorder.Visibility = !_isLaneView && _lastScaleMinutes > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void ToggleLaneView_Checked(object sender, RoutedEventArgs e) {
        _isLaneView = true;
        ResultsControl.ItemTemplate = (DataTemplate)Resources["LaneTemplate"];
        UpdateRulerHeaderVisibility();
    }

    private void ToggleLaneView_Unchecked(object sender, RoutedEventArgs e) {
        _isLaneView = false;
        ResultsControl.ItemTemplate = (DataTemplate)Resources["GanttPairTemplate"];
        UpdateRulerHeaderVisibility();
    }

    private record ResultGroup(string Seiban, DateOnly? LatestActualDate, int PlannedQuantity, IReadOnlyList<OrderProcess> StandardProcesses, IReadOnlyList<OrderProcess> ActualProcesses) {
        // 標準・実績バー共通の全幅（分）。検索結果全体を通した最大値（BtnSearch_Clickで計算）を全行で共有することで、
        // 注文をまたいでも「1時間」「1日」の幅が揃う
        public double ScaleMinutes { get; init; }
        public IReadOnlyList<ProcessLane> Lanes { get; init; } = [];

        public string StandardTotalText => $"{StandardProcesses.Sum(p => p.RequiredMinutes) / 60.0:F1}h";
        public string ActualTotalText => $"{ActualProcesses.Sum(p => p.ActualWorkMinutes) / 60.0:F1}h";
    }

    private record ProcessLane(string ProcessName, double StandardMinutes, double ActualMinutes, double ActualWithinStandardSize, double ActualOverflowSize, string WorkerName) {
        public string ActualTooltip => string.IsNullOrEmpty(WorkerName)
            ? $"実績 {ActualMinutes:F1}分 / 標準 {StandardMinutes:F1}分"
            : $"実績 {ActualMinutes:F1}分 / 標準 {StandardMinutes:F1}分\n担当: {WorkerName}";

        public string ActualMinutesText => $"{ActualMinutes:F1}分";
        public string StandardComparisonText => $" / 標準{StandardMinutes:F1}分";
        // 標準を超過している場合、実績分数の文字色もバーの超過色（赤系）に合わせる
        public Brush ActualTextBrush => ActualOverflowSize > 0 ? Brushes.Firebrick : Brushes.Black;
    }
}
