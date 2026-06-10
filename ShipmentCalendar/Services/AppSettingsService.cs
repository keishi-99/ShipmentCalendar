using ShipmentCalendar.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShipmentCalendar.Services;

/// <summary>アプリ設定のロード・保存を管理する</summary>
public class AppSettingsService
{
    private static readonly string DataDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "data");

    private static readonly string SettingsPath = Path.Combine(DataDir, "appsettings.json");

    // 旧パス（exeと同じ場所）から新パス（data/）へ移行する
    static AppSettingsService()
    {
        Directory.CreateDirectory(DataDir);
        var oldPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (File.Exists(oldPath) && !File.Exists(SettingsPath))
            File.Move(oldPath, SettingsPath);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    // DPAPIで暗号化済みの値であることを示すプレフィックス
    private const string EncryptedPrefix = "enc:";

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.OdbcUserId = Unprotect(settings.OdbcUserId);
            settings.OdbcPassword = Unprotect(settings.OdbcPassword);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        // 呼び出し元のオブジェクトを変更しないよう、暗号化済みの値を持つコピーを作成してから保存する
        var toSave = new AppSettings
        {
            OdbcConnectionMode = settings.OdbcConnectionMode,
            OdbcDsn = settings.OdbcDsn,
            OdbcServer = settings.OdbcServer,
            OdbcPort = settings.OdbcPort,
            OdbcDatabase = settings.OdbcDatabase,
            OdbcUserId = Protect(settings.OdbcUserId),
            OdbcPassword = Protect(settings.OdbcPassword),
            AutoRefreshMinutes = settings.AutoRefreshMinutes,
            DeliveryDateRangeDays = settings.DeliveryDateRangeDays,
            DeliveryDatePastDays = settings.DeliveryDatePastDays
        };

        var json = JsonSerializer.Serialize(toSave, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    /// <summary>DPAPI（CurrentUserスコープ）で文字列を暗号化する</summary>
    private static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainText), null, DataProtectionScope.CurrentUser);
        return EncryptedPrefix + Convert.ToBase64String(encrypted);
    }

    /// <summary>DPAPIで暗号化された文字列を復号する。
    /// 旧形式（平文）の値はそのまま返し、次回保存時に暗号化形式へ移行する。
    /// 別PCへのコピー等で復号に失敗した場合は空文字を返す。</summary>
    private static string Unprotect(string? storedValue)
    {
        if (string.IsNullOrEmpty(storedValue))
            return string.Empty;

        if (!storedValue.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            return storedValue;

        try
        {
            var encrypted = Convert.FromBase64String(storedValue[EncryptedPrefix.Length..]);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return string.Empty;
        }
    }
}
