using ShipmentCalendar.Models;
using System.Data.Odbc;

namespace ShipmentCalendar.Services;

/// <summary>ODBC接続を生成するファクトリ（DSN方式）</summary>
public static class OdbcConnectionFactory
{
    private static string BuildConnectionString(AppSettings settings)
    {
        var builder = new OdbcConnectionStringBuilder
        {
            ["DSN"] = settings.OdbcDsn
        };
        return builder.ConnectionString;
    }

    public static OdbcConnection Create(AppSettings settings)
        => new(BuildConnectionString(settings));

    /// <summary>接続テスト。成功時は null、失敗時はエラーメッセージを返す</summary>
    public static string? Test(AppSettings settings)
    {
        try
        {
            using var conn = new OdbcConnection(BuildConnectionString(settings));
            conn.Open();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
