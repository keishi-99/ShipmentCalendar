using ShipmentCalendar.Models;
using System.IO;
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

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
