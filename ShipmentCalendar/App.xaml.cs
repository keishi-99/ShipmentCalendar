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

        DatabaseInitializer.Initialize();

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
    }
}
