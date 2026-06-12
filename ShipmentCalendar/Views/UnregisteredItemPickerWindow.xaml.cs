using System.ComponentModel;
using System.Windows;

namespace ShipmentCalendar.Views;

/// <summary>未登録品目選択用の一覧データ（番号 + 品目名 + 選択状態）</summary>
public class UnregisteredItemEntry : INotifyPropertyChanged
{
    public string ItemNumber { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>指示工程情報にあるが半製品未登録の品目番号を複数選択するウィンドウ</summary>
public partial class UnregisteredItemPickerWindow : Window
{
    private readonly List<UnregisteredItemEntry> _allItems;

    /// <summary>選択された品目番号（キャンセル時はnull）</summary>
    public List<string>? SelectedItemNumbers { get; private set; }

    public UnregisteredItemPickerWindow(IEnumerable<UnregisteredItemEntry> items)
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

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _allItems)
            item.IsSelected = true;
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _allItems)
            item.IsSelected = false;
    }

    private void BtnRegister_Click(object sender, RoutedEventArgs e)
    {
        var selected = _allItems.Where(i => i.IsSelected).Select(i => i.ItemNumber).ToList();
        if (!selected.Any())
        {
            MessageBox.Show("品目番号を1件以上選択してください", "選択エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedItemNumbers = selected;
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
