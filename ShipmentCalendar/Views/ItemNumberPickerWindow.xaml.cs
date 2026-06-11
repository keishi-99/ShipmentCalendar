using System.Windows;
using System.Windows.Input;

namespace ShipmentCalendar.Views;

/// <summary>品目番号選択用の一覧データ（番号 + 品目名）</summary>
public class ItemPickerEntry
{
    public string ItemNumber { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>検索ボックス付きで登録済み品目番号を一覧から選択するウィンドウ</summary>
public partial class ItemNumberPickerWindow : Window
{
    private readonly List<ItemPickerEntry> _allItems;

    /// <summary>選択された品目番号（キャンセル時はnull）</summary>
    public string? SelectedItemNumber { get; private set; }

    public ItemNumberPickerWindow(IEnumerable<ItemPickerEntry> items)
    {
        InitializeComponent();
        _allItems = items.ToList();
        LstItems.ItemsSource = _allItems;
    }

    /// <summary>入力テキストで品目番号・品目名を部分一致フィルタリング</summary>
    private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var keyword = TxtSearch.Text.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            LstItems.ItemsSource = _allItems;
            return;
        }

        LstItems.ItemsSource = _allItems
            .Where(i => i.ItemNumber.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                     || i.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void LstItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Confirm();
    }

    private void BtnSelect_Click(object sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void Confirm()
    {
        if (LstItems.SelectedItem is not ItemPickerEntry entry) return;
        SelectedItemNumber = entry.ItemNumber;
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
