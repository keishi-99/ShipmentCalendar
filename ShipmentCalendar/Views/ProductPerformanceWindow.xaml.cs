using ShipmentCalendar.Models;
using ShipmentCalendar.Repositories;
using ShipmentCalendar.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ShipmentCalendar.Views;

public partial class ProductPerformanceWindow : Window {
    private const double LaneBarMaxSize = 200.0;

    private readonly AppSettings _settings;
    private List<ItemPickerEntry> _registeredItems = [];
    private Task? _refreshTask;
    private string? _selectedItemNumber;
    private string? _searchedItemNumber;
    private double _lastScaleMinutes;
    private List<ProcessDefinition> _lastDefs = [];
    private List<ResultGroup> _lastGroups = [];

    public ProductPerformanceWindow(AppSettings settings) {
        InitializeComponent();
        _settings = settings;
        // DataTemplate内（ItemsControl配下）のProcessBarControlはXAMLから個別にBindingできないため、
        // 継承プロパティとしてWindow自身にセットし、ビジュアルツリーの子孫全体に伝播させる
        SetValue(ProcessBarControl.DayMinutesProperty, (double)settings.DayMinutes);
        StartDatePicker.SelectedDate = DateTime.Today.AddDays(-90);
        EndDatePicker.SelectedDate = DateTime.Today;
        ViewModeCombo.SelectedIndex = 0;
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
        TxtSelectedItem.Text = itemNumber;
        TxtSelectedItemName.Text = entry?.DisplayName ?? "";
        TxtSelectedItemName.ToolTip = entry?.DisplayName;
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

        BtnSearch.IsEnabled = false;
        SearchProgressBar.Visibility = Visibility.Visible;
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

            // 注文詳細ウィンドウを開くために必要な、製番ごとの品目名・納期・機種コード
            var orderInfoBySeiban = rows
                .GroupBy(r => r.Seiban, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (g.First().ProductName, g.First().DeliveryDate, g.First().ModelCode), StringComparer.OrdinalIgnoreCase);

            var groups = actualByGroup
                .Select(kv => new ResultGroup(
                    kv.Key,
                    kv.Value.Max(p => p.ActualDate),
                    plannedQuantityBySeiban.GetValueOrDefault(kv.Key, 1),
                    BuildStandardProcesses(defs, plannedQuantityBySeiban.GetValueOrDefault(kv.Key, 1), _settings.DayMinutes),
                    kv.Value.OrderBy(p => p.SortOrder).ToList(),
                    orderInfoBySeiban.GetValueOrDefault(kv.Key).ProductName ?? "",
                    orderInfoBySeiban.GetValueOrDefault(kv.Key).DeliveryDate,
                    orderInfoBySeiban.GetValueOrDefault(kv.Key).ModelCode ?? ""))
                .OrderByDescending(g => g.LatestActualDate)
                .ToList();

            _lastDefs = defs;

            // 標準・実績バー共通の全幅は、検索結果全体を通した最大値を使うことで「1日」の幅を注文間で揃える
            var maxScaleMinutes = groups.Count == 0 ? 0.0 : RoundToDayBoundary(groups.Max(g => ComputeRawScaleMinutes(g, _settings.DayMinutes)), _settings.DayMinutes);
            groups = groups.Select(g => g with { ScaleMinutes = maxScaleMinutes, Lanes = BuildLanes(g) }).ToList();

            _lastScaleMinutes = maxScaleMinutes;
            _lastGroups = groups;
            _searchedItemNumber = itemNumber;
            RebuildDayRulerHeader(maxScaleMinutes);
            UpdateRulerHeaderVisibility();

            ResultsControl.ItemsSource = groups;
            if (ViewModeCombo.SelectedIndex == 2)
                RebuildMatrixColumns();

            TxtStatus.Text = groups.Count == 0 ? "該当する実績がありません" : $"{groups.Count} 件表示";
        } catch (Exception ex) {
            TxtStatus.Text = $"検索に失敗しました: {ex.Message}";
        } finally {
            BtnSearch.IsEnabled = true;
            SearchProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    // 標準工数バーは実際の日付を持たないため、休日カレンダー無し・当日基準の仮の注文でBuildProcessesを再利用して組み立てる
    // （バー幅はRequiredMinutesのみで決まるため、日付ラベルに休日が反映されない点を除き表示には影響しない）
    private static List<OrderProcess> BuildStandardProcesses(IEnumerable<ProcessDefinition> defs, int plannedQuantity, int dayMinutes) {
        var calculator = new BusinessDayCalculator([], dayMinutes);
        var dummyOrder = new Order { CompletionDate = DateOnly.FromDateTime(DateTime.Today), PlannedQuantity = plannedQuantity };
        return calculator.BuildProcesses(dummyOrder, defs, new Dictionary<string, (DateOnly?, string, double)>());
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // 注文カード（タイムライン表示・レーン表示どちらも）をダブルクリックしたら、その製番のOrderDetailWindowを開く
    private async void OrderCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement { DataContext: ResultGroup group }) return;
        await OpenOrderDetailAsync(group);
    }

    // 工程比較表の行をダブルクリックしたら、クリック位置から行のDataGridRowを辿ってその製番のOrderDetailWindowを開く
    private async void MatrixGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not { Item: ResultGroup group }) return;
        await OpenOrderDetailAsync(group);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject {
        while (current != null) {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // 検索時にキャッシュしたdefs・受入実績から、MainViewModelと同様の手順でOrderProcessを組み立てる（休日も考慮する）
    private async Task OpenOrderDetailAsync(ResultGroup group) {
        List<Holiday> holidays;
        try {
            holidays = (await new SqliteHolidayRepository().GetAllAsync()).ToList();
        } catch (Exception ex) {
            MessageBox.Show($"休日情報の取得に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var calculator = new BusinessDayCalculator(holidays, _settings.DayMinutes);
        var deliveryDate = group.DeliveryDate ?? DateOnly.FromDateTime(DateTime.Today);
        var itemLeadDays = await SqliteProductDisplayNameRepository.GetCompletionDateLeadDaysAsync(_searchedItemNumber ?? "")
            ?? _settings.CompletionDateLeadDays;
        var order = new Order {
            ProductName = group.ProductName,
            ItemNumber = _searchedItemNumber ?? "",
            ModelCode = group.ModelCode,
            ManufactureNumber = group.Seiban,
            DeliveryDate = deliveryDate,
            CompletionDate = calculator.SubtractBusinessDays(deliveryDate, itemLeadDays),
            PlannedQuantity = group.PlannedQuantity
        };

        var completedByDestNumber = group.ActualProcesses
            .ToDictionary(p => p.DestinationCode, p => (p.ActualDate, p.WorkerName, p.ActualWorkMinutes), StringComparer.OrdinalIgnoreCase);

        order.Processes = calculator.BuildProcesses(order, _lastDefs.Where(d => d.IsVisible), completedByDestNumber);

        // 順序999（最終受入）が完了している場合、前工程すべてを完了扱いにする（MainViewModelと同じ規則）
        var def999 = _lastDefs.FirstOrDefault(d => d.SortOrder == 999);
        if (def999 != null && completedByDestNumber.ContainsKey(def999.DestinationCode)) {
            foreach (var process in order.Processes)
                process.Status = ProcessStatus.Completed;
        }

        new OrderDetailWindow(order, _settings.ShowRequiredTimeInMinutes, _settings.DayMinutes) { Owner = this }.ShowDialog();
    }

    // 標準工数の合計に1営業日分の余白を足した値（丸め前）
    // 実績が標準を大幅に超過している場合、標準工数だけを基準にするとスケールが崩れて実績バーが
    // 正しく比較できなくなるため、標準・実績のうち大きい方の合計を基準にする
    private static double ComputeRawScaleMinutes(ResultGroup group, int dayMinutes) {
        var standardMinutes = group.StandardProcesses.Sum(p => p.RequiredMinutes + p.DwellTimeMinutes + p.OutsourceLeadDays * dayMinutes);
        var actualMinutes = group.ActualProcesses.Sum(p => p.RequiredMinutes + p.DwellTimeMinutes + p.OutsourceLeadDays * dayMinutes);
        return Math.Max(standardMinutes, actualMinutes) + dayMinutes;
    }

    // dayMinutes単位に丸める。単純に切り上げるだけだと端数が小さいときに「4日目がほんの少しだけ」のような
    // 見づらい端切れが出るため、dayMinutesで割った余りが半日を超える場合のみ次の日に切り上げ、
    // 半日以下なら切り捨てて端切れを消す
    private static double RoundToDayBoundary(double minutes, int dayMinutes) {
        var remainder = minutes % dayMinutes;
        return remainder > dayMinutes / 2.0 ? minutes + (dayMinutes - remainder) : minutes - remainder;
    }

    // 工程（指示先番号）ごとに標準・実績の分数をペアにしたレーンを作る。標準は固定長（100%基準）の背景バーとして表現し、
    // 実績バーはその上に重ねて表示する。標準以内の部分と、標準を超えた部分を別の長さとして分けて持たせることで、
    // XAML側で色分けした2色バーとして描ける
    private static List<ProcessLane> BuildLanes(ResultGroup group) {
        var actualByDestination = group.ActualProcesses.ToDictionary(p => p.DestinationCode, StringComparer.OrdinalIgnoreCase);

        var lanes = group.StandardProcesses
            .OrderBy(p => p.SortOrder)
            .Select(std => {
                var hasActual = actualByDestination.TryGetValue(std.DestinationCode, out var actual);
                var actualMinutes = hasActual ? actual!.ActualWorkMinutes : 0.0;
                var (withinStandardSize, overflowSize) = ComputeBarSizes(std.RequiredMinutes, actualMinutes);
                return new ProcessLane(
                    std.ProcessName,
                    std.RequiredMinutes,
                    actualMinutes,
                    withinStandardSize,
                    overflowSize,
                    hasActual ? actual!.WorkerName : "");
            })
            .ToList();

        // 合計レーン: 各工程バーと同じ「実績÷標準」の比率式を使うため、既存のProcessLane描画をそのまま流用できる
        var standardTotal = group.StandardProcesses.Sum(p => p.RequiredMinutes);
        var actualTotal = group.ActualProcesses.Sum(p => p.ActualWorkMinutes);
        var (totalWithinStandardSize, totalOverflowSize) = ComputeBarSizes(standardTotal, actualTotal);
        lanes.Add(new ProcessLane("合計", standardTotal, actualTotal, totalWithinStandardSize, totalOverflowSize, "", IsTotal: true));

        return lanes;
    }

    // 標準工数が0分（未設定の工程等）で実績だけがある場合、比率計算では常に0になり実績バーが
    // 見えなくなってしまうため、超過（警告色）として最大幅で表示し実績の存在を示す。
    // 超過分自体は標準の200%（LaneBarMaxSize分）を上限にする。実際の数値は常時表示のラベル側で
    // 正確に伝わるため、視覚上の長さだけ頭打ちにしても情報は失われない
    private static (double withinStandardSize, double overflowSize) ComputeBarSizes(double standardMinutes, double actualMinutes) {
        if (standardMinutes <= 0) return (0.0, actualMinutes > 0 ? LaneBarMaxSize : 0.0);
        var within = Math.Min(actualMinutes, standardMinutes) / standardMinutes * LaneBarMaxSize;
        var overflow = Math.Min(LaneBarMaxSize, Math.Max(0, actualMinutes - standardMinutes) / standardMinutes * LaneBarMaxSize);
        return (within, overflow);
    }

    // タイムライン表示専用の共通日数ルーラーを1回だけ組み立てる（全注文がScaleMinutesを共有しているため）。
    // ProcessBarControlの相対日数ルーラーと同じ考え方（DayMinutes＝1営業日ごとに区切り、交互背景で「n日目」を表示）
    private void RebuildDayRulerHeader(double totalMinutes) {
        DayRulerGrid.ColumnDefinitions.Clear();
        DayRulerGrid.Children.Clear();
        if (totalMinutes <= 0) return;

        var brushA = (Brush)Application.Current.Resources["DateBarBackgroundBrushA"];
        var brushB = (Brush)Application.Current.Resources["DateBarBackgroundBrushB"];
        var borderBrush = new SolidColorBrush(Color.FromRgb(154, 176, 204));

        var dayCount = Math.Max(1, (int)Math.Ceiling(totalMinutes / _settings.DayMinutes));
        var remaining = totalMinutes;
        for (int day = 0; day < dayCount && remaining >= 1; day++) {
            var width = Math.Min(_settings.DayMinutes, remaining);
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
        RulerHeaderBorder.Visibility = ViewModeCombo.SelectedIndex == 0 && _lastScaleMinutes > 0 ? Visibility.Visible : Visibility.Collapsed;

    // 表示形式: 0=タイムライン表示, 1=工程別レーン表示, 2=工程比較表
    private void ViewModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        var index = ViewModeCombo.SelectedIndex;

        MatrixGrid.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        ResultsScrollViewer.Visibility = index == 2 ? Visibility.Collapsed : Visibility.Visible;

        if (index != 2)
            ResultsControl.ItemTemplate = (DataTemplate)Resources[index == 1 ? "LaneTemplate" : "GanttPairTemplate"];
        else
            RebuildMatrixColumns();

        UpdateRulerHeaderVisibility();
    }

    // 工程比較表の列（製番等の固定列＋工程ごとの動的列）を組み立てる
    private void RebuildMatrixColumns() {
        MatrixGrid.Columns.Clear();
        MatrixGrid.Columns.Add(new DataGridTextColumn { Header = "製番", Binding = new Binding(nameof(ResultGroup.Seiban)), Width = 90 });
        MatrixGrid.Columns.Add(new DataGridTextColumn { Header = "計画数", Binding = new Binding(nameof(ResultGroup.PlannedQuantity)), Width = 60 });

        // group.LanesはStandardProcesses（=defs全件）をSortOrder順に並べたものなので、
        // 列のインデックスと合わせるためdefsも同じ順序で並べる
        var orderedDefs = _lastDefs.OrderBy(d => d.SortOrder).ToList();
        for (int i = 0; i < orderedDefs.Count; i++)
            MatrixGrid.Columns.Add(BuildMatrixProcessColumn(orderedDefs[i].ProcessName, i));

        // 合計列: BuildLanesが工程レーンの末尾に追加した合計レーンを、そのままインデックスorderedDefs.Countで参照する
        MatrixGrid.Columns.Add(BuildMatrixProcessColumn("合計", orderedDefs.Count, isTotal: true));

        MatrixGrid.ItemsSource = _lastGroups;
    }

    // 工程1つ分の比較セル列を作る。標準を固定長の背景バー、実績をその上に重ねたバーとして表示する。
    // レーン表示とProcessLaneを共有しているため、ScaleTransformではなくバーの幅だけをHalfSizeConverterで半分にすることで
    // 表側に収める（文字はレーン表示と同じフォントサイズのまま、スケールによるにじみもない）
    // 実績が標準の200%まで超過した場合、オーバーレイバーは(LaneBarMaxSize×2)×0.5=200pxまで伸びうるため、
    // それが列内に収まるよう列幅を確保する（収まらないとDataGridは既定でクリップしないため隣列に重なって見えてしまう）
    private DataGridTemplateColumn BuildMatrixProcessColumn(string header, int laneIndex, bool isTotal = false) {
        var column = new DataGridTemplateColumn { Header = header, Width = LaneBarMaxSize + 10 };
        var template = new DataTemplate();

        // 合計列は通常の工程列と区別するため、左側に区切り線とヘッダー太字を入れる
        var rootFactory = new FrameworkElementFactory(typeof(Border));
        if (isTotal) {
            rootFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
            rootFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1, 0, 0, 0));
            rootFactory.SetValue(Border.PaddingProperty, new Thickness(6, 0, 0, 0));

            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
            column.HeaderStyle = headerStyle;
        }

        var containerFactory = new FrameworkElementFactory(typeof(Grid));
        containerFactory.SetBinding(FrameworkElement.DataContextProperty, new Binding(nameof(ResultGroup.Lanes)) {
            Converter = new LaneByIndexConverter(),
            ConverterParameter = laneIndex
        });

        var backgroundFactory = new FrameworkElementFactory(typeof(Border));
        backgroundFactory.SetValue(FrameworkElement.WidthProperty, LaneBarMaxSize / 2);
        backgroundFactory.SetValue(FrameworkElement.HeightProperty, 8.0);
        backgroundFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        backgroundFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xDC, 0xE3, 0xEC)));
        backgroundFactory.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(ProcessLane.StandardMinutes)) { StringFormat = "標準 {0:F1}分" });

        var overlayFactory = new FrameworkElementFactory(typeof(StackPanel));
        overlayFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        overlayFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);

        var withinFactory = new FrameworkElementFactory(typeof(Border));
        withinFactory.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(ProcessLane.ActualWithinStandardSize)) { Converter = new HalfSizeConverter() });
        withinFactory.SetValue(FrameworkElement.HeightProperty, 8.0);
        withinFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x66, 0x99, 0xCC)));
        withinFactory.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(ProcessLane.ActualTooltip)));

        var overflowFactory = new FrameworkElementFactory(typeof(Border));
        overflowFactory.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(ProcessLane.ActualOverflowSize)) { Converter = new HalfSizeConverter() });
        overflowFactory.SetValue(FrameworkElement.HeightProperty, 8.0);
        overflowFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0xB3, 0xD9, 0x53, 0x4F)));
        overflowFactory.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(ProcessLane.ActualTooltip)));

        overlayFactory.AppendChild(withinFactory);
        overlayFactory.AppendChild(overflowFactory);

        // ラベルはバーと同じ位置に固定オフセットで重ね、実績が超過してバーが伸びても位置がズレないようにする（レーン表示と同じ考え方）。
        // オフセットもバーの半分幅（LaneBarMaxSize/2）に合わせて半分にしてある
        var labelFactory = new FrameworkElementFactory(typeof(StackPanel));
        labelFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(LaneBarMaxSize / 2 + 5, 0, 0, 0));
        labelFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        labelFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        labelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var actualTextFactory = new FrameworkElementFactory(typeof(TextBlock));
        actualTextFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
        actualTextFactory.SetValue(TextBlock.FontWeightProperty, isTotal ? FontWeights.Bold : FontWeights.SemiBold);
        actualTextFactory.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ProcessLane.ActualTextBrush)));
        actualTextFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProcessLane.ActualMinutesText)));

        var standardTextFactory = new FrameworkElementFactory(typeof(TextBlock));
        standardTextFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
        standardTextFactory.SetValue(TextBlock.FontWeightProperty, isTotal ? FontWeights.Bold : FontWeights.Normal);
        standardTextFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)));
        standardTextFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProcessLane.StandardComparisonText)));

        labelFactory.AppendChild(actualTextFactory);
        labelFactory.AppendChild(standardTextFactory);

        containerFactory.AppendChild(backgroundFactory);
        containerFactory.AppendChild(overlayFactory);
        containerFactory.AppendChild(labelFactory);

        rootFactory.AppendChild(containerFactory);
        template.VisualTree = rootFactory;
        column.CellTemplate = template;
        return column;
    }

    // ResultGroup.Lanes（IReadOnlyList<ProcessLane>）から、列ごとに固定されたインデックスの要素を取り出す
    private class LaneByIndexConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) {
            if (value is not IReadOnlyList<ProcessLane> lanes || parameter is not int index) return null;
            return index >= 0 && index < lanes.Count ? lanes[index] : null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    // 工程比較表のバー幅を、レーン表示と共有しているProcessLaneの値を変えずに半分にする
    private class HalfSizeConverter : IValueConverter {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is double d ? d / 2 : 0.0;

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private record ResultGroup(string Seiban, DateOnly? LatestActualDate, int PlannedQuantity, IReadOnlyList<OrderProcess> StandardProcesses, IReadOnlyList<OrderProcess> ActualProcesses, string ProductName, DateOnly? DeliveryDate, string ModelCode) {
        // 標準・実績バー共通の全幅（分）。検索結果全体を通した最大値（BtnSearch_Clickで計算）を全行で共有することで、
        // 注文をまたいでも「1時間」「1日」の幅が揃う
        public double ScaleMinutes { get; init; }
        public IReadOnlyList<ProcessLane> Lanes { get; init; } = [];

        public string StandardTotalText => $"{StandardProcesses.Sum(p => p.RequiredMinutes) / 60.0:F1}h";
        public string ActualTotalText => $"{ActualProcesses.Sum(p => p.ActualWorkMinutes) / 60.0:F1}h";

        // 順序999（最終受入）が品目に定義されているのに、まだ実績が無い場合は作業途中とみなす
        public bool IsInProgress => StandardProcesses.Any(p => p.SortOrder == 999) && !ActualProcesses.Any(p => p.SortOrder == 999);
    }

    private record ProcessLane(string ProcessName, double StandardMinutes, double ActualMinutes, double ActualWithinStandardSize, double ActualOverflowSize, string WorkerName, bool IsTotal = false) {
        public string ActualTooltip => string.IsNullOrEmpty(WorkerName)
            ? $"実績 {ActualMinutes:F1}分 / 標準 {StandardMinutes:F1}分"
            : $"実績 {ActualMinutes:F1}分 / 標準 {StandardMinutes:F1}分\n担当: {WorkerName}";

        public string ActualMinutesText => $"{ActualMinutes:F1}分";
        public string StandardComparisonText => $" / {StandardMinutes:F1}分";
        // 標準を超過している場合、実績分数の文字色もバーの超過色（赤系）に合わせる。
        // バー自体は半透明の赤（#B3D9534F）にしているため、文字はそれより濃い暗めの赤にしてコントラストを確保する
        public Brush ActualTextBrush => ActualOverflowSize > 0 ? new SolidColorBrush(Color.FromRgb(0x7A, 0x1A, 0x1A)) : Brushes.Black;
    }
}
