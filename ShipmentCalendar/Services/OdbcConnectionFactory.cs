using ShipmentCalendar.Models;
using System.Data.Odbc;

namespace ShipmentCalendar.Services;

/// <summary>ODBC接続を生成するファクトリ（DSN方式）</summary>
public static class OdbcConnectionFactory
{
    public static OdbcConnection Create(AppSettings settings)
        => new OdbcConnection($"DSN={settings.OdbcDsn};Uid={settings.OdbcUserId};Pwd={settings.OdbcPassword};");

    /// <summary>接続テスト。成功時は null、失敗時はエラーメッセージを返す</summary>
    public static string? Test(string dsn, string userId, string password)
    {
        try
        {
            using var conn = new OdbcConnection($"DSN={dsn};Uid={userId};Pwd={password};");
            conn.Open();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
