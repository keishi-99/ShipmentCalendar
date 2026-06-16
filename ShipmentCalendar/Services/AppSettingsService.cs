using ShipmentCalendar.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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

    // DPAPIで暗号化済みの値であることを示すプレフィックス
    private const string EncryptedPrefix = "enc:";

    public static AppSettings Load() {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.OdbcUserId = Unprotect(settings.OdbcUserId);
            settings.OdbcPassword = Unprotect(settings.OdbcPassword);
            return settings;
        } catch {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings) {
        ArgumentNullException.ThrowIfNull(settings);

        // 呼び出し元のオブジェクトを変更しないよう、シリアライズ経由でコピーを作成してから暗号化して保存する
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        var toSave = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        toSave.OdbcUserId = Protect(settings.OdbcUserId);
        toSave.OdbcPassword = Protect(settings.OdbcPassword);

        var encryptedJson = JsonSerializer.Serialize(toSave, _jsonOptions);
        File.WriteAllText(_settingsPath, encryptedJson);
    }

    /// <summary>DPAPI（CurrentUserスコープ）で文字列を暗号化する</summary>
    private static string Protect(string? plainText) {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainText), null, DataProtectionScope.CurrentUser);
        return EncryptedPrefix + Convert.ToBase64String(encrypted);
    }

    /// <summary>DPAPIで暗号化された文字列を復号する。
    /// 旧形式（平文）の値はそのまま返し、次回保存時に暗号化形式へ移行する。
    /// 別PCへのコピー等で復号に失敗した場合は空文字を返す。</summary>
    private static string Unprotect(string? storedValue) {
        if (string.IsNullOrEmpty(storedValue))
            return string.Empty;

        if (!storedValue.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            return storedValue;

        try {
            var encrypted = Convert.FromBase64String(storedValue[EncryptedPrefix.Length..]);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        } catch {
            return string.Empty;
        }
    }
}
