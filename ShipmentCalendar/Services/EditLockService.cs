using System.IO;
using System.Text.Json;

namespace ShipmentCalendar.Services;

/// <summary>複数PCからの同時編集を防ぐための共有ロック（共有データフォルダにロックファイルを作成して管理する）</summary>
public static class EditLockService {
    private static readonly string? _lockPath = AppSettingsService.GetSharedDataDir() is { } dir ? Path.Combine(dir, "edit.lock") : null;

    /// <summary>この時間だけハートビートが更新されなければ、クラッシュ等で残ったロックとみなして上書きを許可する</summary>
    private static readonly TimeSpan StaleTimeout = TimeSpan.FromMinutes(30);

    private static bool _heldByThisProcess;

    private record LockInfo(string UserName, string MachineName, DateTime LastHeartbeat);

    public readonly record struct AcquireResult(bool Acquired, string? HeldByMessage);

    /// <summary>編集ロックの取得を試みる。取得できない場合は、現在の保持者を説明するメッセージを返す。
    /// 「存在しなければ作成」をOSレベルでアトミックに行うことに加え、既存ロックの引き継ぎ判定も
    /// 排他ハンドルを保持したまま行うことで、複数PCがほぼ同時に取得を試みても
    /// どちらか一方しか成功しないようにする</summary>
    public static AcquireResult TryAcquire() {
        if (_lockPath == null)
            return new AcquireResult(false, "共有データフォルダが設定されていません。設定 > 基本設定 から設定してください。");

        Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);

        var info = new LockInfo(Environment.UserName, Environment.MachineName, DateTime.Now);
        if (TryCreateLockFileExclusively(info)) {
            _heldByThisProcess = true;
            return new AcquireResult(true, null);
        }

        if (TryTakeOverIfNotActivelyHeld(info, out var heldByMessage)) {
            _heldByThisProcess = true;
            return new AcquireResult(true, null);
        }

        return new AcquireResult(false, heldByMessage ?? "編集中のためロックできません（他のPCと取得のタイミングが重なりました。もう一度お試しください）。");
    }

    /// <summary>編集継続中であることを示すため、ロックファイルの最終更新時刻を更新する（長時間の編集がタイムアウトで奪われないようにする）。
    /// 既に他PCに正当に引き継がれている場合は、誤って上書きせずfalseを返す</summary>
    public static bool RefreshHeartbeat() {
        if (!_heldByThisProcess) return false;

        var refreshed = TryRefreshIfStillOwned(new LockInfo(Environment.UserName, Environment.MachineName, DateTime.Now));
        if (!refreshed) _heldByThisProcess = false;
        return refreshed;
    }

    /// <summary>編集ロックを解放する。ロックファイルの内容を確認し、自分が取得したロックのままである場合のみ解放する
    /// （タイムアウトで他PCに正当に引き継がれた後は、誤って相手のロックを消してしまわないようにする）</summary>
    public static void Release() {
        if (!_heldByThisProcess) return;
        _heldByThisProcess = false;

        ReleaseIfStillOwned();
    }

    /// <summary>ロックファイルが存在しない場合のみアトミックに作成する。
    /// 既に存在する場合は例外になるため、複数PCが同時に作成を試みても成功するのは1つだけになる</summary>
    private static bool TryCreateLockFileExclusively(LockInfo info) {
        try {
            using var stream = new FileStream(_lockPath!, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(JsonSerializer.Serialize(info));
            return true;
        } catch (IOException) {
            return false;
        }
    }

    /// <summary>ロックファイルを排他アクセスで開いたまま、現在の内容が有効かつ期限内のロックでないことを確認したうえで
    /// 自分のロックとして上書きする（期限切れ・壊れていて読めない・ファイルが既に無い、のいずれの場合も引き継ぎ対象とする）。
    /// ファイルを開いてから書き込むまでを単一の排他ハンドル内で行うことで、他プロセスが同時に同じ判定・書き込みを
    /// 行えないようにする</summary>
    private static bool TryTakeOverIfNotActivelyHeld(LockInfo newInfo, out string? heldByMessage) {
        heldByMessage = null;
        try {
            using var stream = new FileStream(_lockPath!, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var current = TryParseLockInfo(ReadAllText(stream));

            if (current != null && DateTime.Now - current.LastHeartbeat < StaleTimeout) {
                heldByMessage = $"編集中のためロックできません。{current.UserName}さんが{current.MachineName}で編集中です。";
                return false;
            }

            WriteAllText(stream, JsonSerializer.Serialize(newInfo));
            return true;
        } catch (IOException) {
            return false; // 他プロセスが排他アクセス中
        }
    }

    /// <summary>ロックファイルを排他アクセスで開いたまま、自分が現在の所有者であることを確認したうえでハートビートを更新する</summary>
    private static bool TryRefreshIfStillOwned(LockInfo newInfo) {
        try {
            using var stream = new FileStream(_lockPath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var current = TryParseLockInfo(ReadAllText(stream));
            if (current == null || current.UserName != Environment.UserName || current.MachineName != Environment.MachineName)
                return false;

            WriteAllText(stream, JsonSerializer.Serialize(newInfo));
            return true;
        } catch (IOException) {
            return false;
        }
    }

    /// <summary>ロックファイルを排他アクセスで開いたまま、自分が現在の所有者であることを確認したうえで解放する（空にする）</summary>
    private static void ReleaseIfStillOwned() {
        try {
            using var stream = new FileStream(_lockPath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var current = TryParseLockInfo(ReadAllText(stream));
            if (current == null || current.UserName != Environment.UserName || current.MachineName != Environment.MachineName)
                return;

            stream.SetLength(0);
        } catch (IOException) { /* 解放に失敗してもStaleTimeout経過で自然に解放される */ }
    }

    private static LockInfo? TryParseLockInfo(string json) {
        try {
            return JsonSerializer.Deserialize<LockInfo>(json);
        } catch {
            return null;
        }
    }

    private static string ReadAllText(FileStream stream) {
        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static void WriteAllText(FileStream stream, string content) {
        stream.Seek(0, SeekOrigin.Begin);
        stream.SetLength(0);
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write(content);
        writer.Flush();
    }
}
