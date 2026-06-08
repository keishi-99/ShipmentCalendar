using ShipmentCalendar.Models;
using System.Data.Odbc;

namespace ShipmentCalendar.Services;

/// <summary>ODBC接続を生成するファクトリ（DSN方式）</summary>
public static class OdbcConnectionFactory
{
    // パスワード中の } を }} にエスケープして接続文字列インジェクションを防ぐ
    private static string BuildConnectionString(string dsn, string userId, string password)
        => $"DSN={dsn};Uid={userId};Pwd={{{password.Replace("}", "}}")}}};";

    public static OdbcConnection Create(AppSettings settings)
        => new OdbcConnection(BuildConnectionString(settings.OdbcDsn, settings.OdbcUserId, settings.OdbcPassword));

    /// <summary>接続テスト。成功時は null、失敗時はエラーメッセージを返す</summary>
    public static string? Test(string dsn, string userId, string password)
    {
        try
        {
            using var conn = new OdbcConnection(BuildConnectionString(dsn, userId, password));
            conn.Open();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
