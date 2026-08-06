using ShipmentCalendar.Models;
using System.IO;
using System.Text.Json;

namespace ShipmentCalendar.Services;

/// <summary>アプリ設定のロード・保存を管理する</summary>
public static class AppSettingsService {
    private static readonly string _dataDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "data");

    private static readonly string _settingsPath = Path.Combine(_dataDir, "appsettings.json");

    private static readonly JsonSerializerOptions _jsonOptions = new() {
        WriteIndented = true
    };

    public static AppSettings Load() {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        } catch {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings) {
        ArgumentNullException.ThrowIfNull(settings);

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        Directory.CreateDirectory(_dataDir);
        File.WriteAllText(_settingsPath, json);
    }

    private static string? _cachedSharedDataDir;

    /// <summary>マスタDB・編集ロックファイルを配置するフォルダを返す。
    /// 設定で共有フォルダが指定されていればそれを、未設定ならローカルのdataフォルダを返す。
    /// 初回呼び出し時の値をプロセス内でキャッシュし、以降は設定が変更されても同じ値を返す
    /// （DatabaseInitializerとEditLockServiceが、同じプロセス内で常に同じパスを使うようにするため）</summary>
    public static string GetSharedDataDir() {
        if (_cachedSharedDataDir != null) return _cachedSharedDataDir;

        var sharedPath = Load().SharedDataFolderPath;
        _cachedSharedDataDir = string.IsNullOrWhiteSpace(sharedPath) ? _dataDir : sharedPath;
        return _cachedSharedDataDir;
    }
}
