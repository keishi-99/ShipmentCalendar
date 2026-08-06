using System.IO;
using System.Text.Json;

namespace ShipmentCalendar.Services;

/// <summary>複数PCからの同時編集を防ぐための共有ロック（共有データフォルダにロックファイルを作成して管理する。
/// 共有フォルダが未設定の場合はローカルのdataフォルダを使用する）</summary>
public static class EditLockService {
    private static readonly string _lockPath = Path.Combine(AppSettingsService.GetSharedDataDir(), "edit.lock");

    /// <summary>この時間だけハートビートが更新されなければ、クラッシュ等で残ったロックとみなして上書きを許可する</summary>
    private static readonly TimeSpan StaleTimeout = TimeSpan.FromMinutes(30);

    private static bool _heldByThisProcess;

    private record LockInfo(string UserName, string MachineName, DateTime LastHeartbeat);

    public readonly record struct AcquireResult(bool Acquired, string? HeldByMessage);

    /// <summary>編集ロックの取得を試みる。取得できない場合は、現在の保持者を説明するメッセージを返す。
    /// 「存在しなければ作成」をOSレベルでアトミックに行うことで、複数PCがほぼ同時に取得を試みても
    /// どちらか一方しか成功しないようにする（読み込み→判定→書き込みの間に競合状態が生まれるのを防ぐ）</summary>
    public static AcquireResult TryAcquire() {
        Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);

        var info = new LockInfo(Environment.UserName, Environment.MachineName, DateTime.Now);
        if (TryCreateLockFileExclusively(info)) {
            _heldByThisProcess = true;
            return new AcquireResult(true, null);
        }

        var existing = ReadLockFile();
        if (existing != null && DateTime.Now - existing.LastHeartbeat < StaleTimeout) {
            return new AcquireResult(false, $"編集中のためロックできません。{existing.UserName}さんが{existing.MachineName}で編集中です。");
        }

        // ここに来るのは、ロックファイルはあるが期限切れ（クラッシュ等で残った）とみなせる場合。
        // 削除の直前にもう一度読み直し、stale判定時と同じ内容のままであることを確認してから削除する
        // （他PCが直前に取得し直した新しいロックを、内容を確認せず削除してしまわないようにするため）
        if (existing != null && ReadLockFile() == existing) {
            try { File.Delete(_lockPath); } catch { /* 他PCが同時に削除・再取得している可能性があるため無視する */ }
        }

        if (!TryCreateLockFileExclusively(info))
            return new AcquireResult(false, "編集中のためロックできません（他のPCと取得のタイミングが重なりました。もう一度お試しください）。");

        _heldByThisProcess = true;
        return new AcquireResult(true, null);
    }

    /// <summary>編集継続中であることを示すため、ロックファイルの最終更新時刻を更新する（長時間の編集がタイムアウトで奪われないようにする）。
    /// 既に他PCに正当に引き継がれている場合は、誤って上書きしないよう何もしない</summary>
    public static void RefreshHeartbeat() {
        if (!_heldByThisProcess) return;
        if (!StillOwnsLock()) { _heldByThisProcess = false; return; }

        WriteLockFile(new LockInfo(Environment.UserName, Environment.MachineName, DateTime.Now));
    }

    /// <summary>編集ロックを解放する。ロックファイルの内容を確認し、自分が取得したロックのままである場合のみ削除する
    /// （タイムアウトで他PCに正当に引き継がれた後は、誤って相手のロックを削除しないようにする）</summary>
    public static void Release() {
        if (!_heldByThisProcess) return;
        _heldByThisProcess = false;

        if (!StillOwnsLock()) return;
        try { File.Delete(_lockPath); } catch { /* 削除に失敗してもStaleTimeout経過で自然に解放される */ }
    }

    /// <summary>ロックファイルの内容が、自分（このユーザー・このマシン）が取得したものと一致するか確認する</summary>
    private static bool StillOwnsLock() {
        var existing = ReadLockFile();
        return existing != null && existing.UserName == Environment.UserName && existing.MachineName == Environment.MachineName;
    }

    /// <summary>ロックファイルが存在しない場合のみアトミックに作成する。
    /// 既に存在する場合は例外になるため、複数PCが同時に作成を試みても成功するのは1つだけになる</summary>
    private static bool TryCreateLockFileExclusively(LockInfo info) {
        try {
            using var stream = new FileStream(_lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(JsonSerializer.Serialize(info));
            return true;
        } catch (IOException) {
            return false;
        }
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
