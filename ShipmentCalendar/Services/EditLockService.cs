using System.IO;
using System.Text.Json;

namespace ShipmentCalendar.Services;

/// <summary>複数PCからの同時編集を防ぐための共有ロック（dataフォルダにロックファイルを作成して管理する）</summary>
public static class EditLockService {
    private static readonly string _lockPath = Path.Combine(AppSettingsService.GetSharedDataDir(), "edit.lock");

    /// <summary>この時間だけハートビートが更新されなければ、クラッシュ等で残ったロックとみなして上書きを許可する</summary>
    private static readonly TimeSpan StaleTimeout = TimeSpan.FromMinutes(30);

    private static bool _heldByThisProcess;

    private record LockInfo(string UserName, string MachineName, DateTime LastHeartbeat);

    public readonly record struct AcquireResult(bool Acquired, string? HeldByMessage);

    /// <summary>編集ロックの取得を試みる。取得できない場合は、現在の保持者を説明するメッセージを返す</summary>
    public static AcquireResult TryAcquire() {
        Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);

        var existing = ReadLockFile();

        if (existing != null) {
            var now = DateTime.Now;
            if (now - existing.LastHeartbeat < StaleTimeout) {
                return new AcquireResult(false, $"編集中のためロックできません。{existing.UserName}さんが{existing.MachineName}で編集中です。");
            }
        }

        WriteLockFile(new LockInfo(Environment.UserName, Environment.MachineName, DateTime.Now));
        _heldByThisProcess = true;
        return new AcquireResult(true, null);
    }

    /// <summary>編集継続中であることを示すため、ロックファイルの最終更新時刻を更新する（長時間の編集がタイムアウトで奪われないようにする）</summary>
    public static void RefreshHeartbeat() {
        if (!_heldByThisProcess) return;
        WriteLockFile(new LockInfo(Environment.UserName, Environment.MachineName, DateTime.Now));
    }

    /// <summary>編集ロックを解放する</summary>
    public static void Release() {
        if (!_heldByThisProcess) return;
        try { File.Delete(_lockPath); } catch { /* 削除に失敗してもStaleTimeout経過で自然に解放される */ }
        _heldByThisProcess = false;
    }

    private static LockInfo? ReadLockFile() {
        if (!File.Exists(_lockPath)) return null;
        try {
            return JsonSerializer.Deserialize<LockInfo>(File.ReadAllText(_lockPath));
        } catch {
            return null;
        }
    }

    private static void WriteLockFile(LockInfo info) =>
        File.WriteAllText(_lockPath, JsonSerializer.Serialize(info));
}
