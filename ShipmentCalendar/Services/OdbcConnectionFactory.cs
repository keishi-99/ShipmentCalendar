using ShipmentCalendar.Models;
using System.Data.Odbc;

namespace ShipmentCalendar.Services;

/// <summary>ODBC接続を生成するファクトリ（DSN方式 / DSNレス方式）</summary>
public static class OdbcConnectionFactory
{
    private const string DriverName = "Dr.Sum 5.6 ODBC Driver";

    // OdbcConnectionStringBuilder で特殊文字を安全にエスケープ
    private static string BuildConnectionString(AppSettings settings)
    {
        var builder = new OdbcConnectionStringBuilder();
        if (settings.OdbcConnectionMode == "Direct")
        {
            // DSNレス接続：ODBCデータソースアドミニストレーターへの登録不要
            builder["Driver"] = DriverName;
            builder["Server"] = settings.OdbcServer;
            builder["Port"] = settings.OdbcPort;
            builder["Database"] = settings.OdbcDatabase;
        }
        else
        {
            builder["DSN"] = settings.OdbcDsn;
        }
        builder["Uid"] = settings.OdbcUserId;
        builder["Pwd"] = settings.OdbcPassword;
        return builder.ConnectionString;
    }

    public static OdbcConnection Create(AppSettings settings)
        => new OdbcConnection(BuildConnectionString(settings));

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
