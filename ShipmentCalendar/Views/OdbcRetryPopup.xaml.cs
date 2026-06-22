using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace ShipmentCalendar.Views;

/// <summary>受注データ0件検出時に表示する非モーダルなリトライ待機ポップアップ</summary>
public partial class OdbcRetryPopup : Window {
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TaskCompletionSource? _tcs;
    private bool _closeAllowed;
    private bool _cancelled;
    private int _remainingSeconds;
    private int _attemptNumber;
    private int _maxAttempts;

    public OdbcRetryPopup() {
        InitializeComponent();
        Closing += OnClosing;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _timer.Tick += OnTick;
    }

    // メインウィンドウが閉じられる際は、ポップアップが閉じるのを拒否してアプリが終了不能になるのを防ぐため、
    // キャンセル扱いで先にポップアップを閉じる
    private void OnLoaded(object sender, RoutedEventArgs e) {
#pragma warning disable IDE0031 // ?. はイベントの += 左辺に使えずCS0131になるため簡素化できない
        if (Owner != null) Owner.Closing += Owner_Closing;
#pragma warning restore IDE0031
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) {
#pragma warning disable IDE0031 // ?. はイベントの -= 左辺に使えずCS0131になるため簡素化できない
        if (Owner != null) Owner.Closing -= Owner_Closing;
#pragma warning restore IDE0031
    }

    private void Owner_Closing(object? sender, CancelEventArgs e) => Finish(cancelled: true);

    /// <summary>
    /// ポップアップを表示し、指定秒数のカウントダウンを行う。
    /// カウントダウン終了またはキャンセルボタン押下で自動的にCloseしてから返る。
    /// 戻り値: キャンセルボタンが押された場合はtrue（呼び出し元は読み込み処理全体を中止する）
    /// </summary>
    public async Task<bool> ShowAndCountdownAsync(int seconds, int attemptNumber, int maxAttempts) {
        _remainingSeconds = seconds;
        _attemptNumber = attemptNumber;
        _maxAttempts = maxAttempts;
        UpdateMessage();

        _tcs = new TaskCompletionSource();
        Show();
        _timer.Start();

        await _tcs.Task;
        return _cancelled;
    }

    private void OnTick(object? sender, EventArgs e) {
        _remainingSeconds--;
        if (_remainingSeconds <= 0) {
            Finish(cancelled: false);
            return;
        }
        UpdateMessage();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Finish(cancelled: true);

    private void Finish(bool cancelled) {
        _timer.Stop();
        _cancelled = cancelled;
        _closeAllowed = true;
        Close();
        _tcs?.TrySetResult();
    }

    // ×ボタン等での予期しないクローズを防ぎ、キャンセルボタン経由でのみ閉じられるようにする
    private void OnClosing(object? sender, CancelEventArgs e) {
        if (!_closeAllowed) e.Cancel = true;
    }

    private void UpdateMessage() {
        TxtMessage.Text = $"受注データが0件のため、{_remainingSeconds}秒後にリトライします（{_attemptNumber}/{_maxAttempts}回目）";
    }
}
