using ShipmentCalendar.Data;
using System.Threading;
using System.Windows;

namespace ShipmentCalendar;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "ShipmentCalendar_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("すでに起動しています。", "多重起動防止", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Exit += (_, _) => { _mutex?.ReleaseMutex(); _mutex?.Dispose(); };

        // UIスレッドの未処理例外
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // 非同期タスクの未処理例外
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Dispatcher.Invoke(() =>
                MessageBox.Show(args.Exception.InnerException?.Message ?? args.Exception.Message,
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error));
            args.SetObserved();
        };

        // DB初期化は共有ネットワークフォルダに接続する可能性があり失敗しうるため、
        // 未処理例外ハンドラの登録後に呼び出し、生のクラッシュではなくエラーダイアログで通知されるようにする
        DatabaseInitializer.Initialize();

        // 共有データフォルダが未設定の場合、DatabaseInitializer.Initialize()は何もせず終了している。
        // 起動自体は継続させ（設定画面を開けるようにするため）、案内だけ表示する
        if (!DatabaseInitializer.IsAvailable)
        {
            MessageBox.Show(
                "共有データフォルダが設定されていません。設定 > 基本設定 から設定してください。\n設定するまで、工程マスタ・休日・部署などの編集機能は使用できません。",
                "共有データフォルダ未設定", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
