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
}
