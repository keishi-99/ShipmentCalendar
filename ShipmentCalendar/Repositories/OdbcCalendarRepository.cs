using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Repositories;

/// <summary>ODBC経由でVP_カレンダ情報_YDから休日（稼働区分='01'）を取得するリポジトリ</summary>
public class OdbcCalendarRepository(AppSettings settings)
{
    /// <summary>指定年・工場番号の休日（稼働区分='01'）の日付一覧を取得する</summary>
    public IEnumerable<DateOnly> GetHolidays(int year)
    {
        using var conn = OdbcConnectionFactory.Create(settings);
        conn.Open();

        List<DateOnly> dates = [];
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT 日付 FROM VP_カレンダ情報_YD
            WHERE 工場番号 = ?
              AND 稼働区分 = '01'
              AND 日付_数値 BETWEEN ? AND ?";
        cmd.Parameters.Add("@FactoryNumber", System.Data.Odbc.OdbcType.VarChar).Value = settings.OdbcFactoryNumber;
        cmd.Parameters.Add("@From", System.Data.Odbc.OdbcType.Int).Value = year * 10000 + 101;
        cmd.Parameters.Add("@To", System.Data.Odbc.OdbcType.Int).Value = year * 10000 + 1231;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // 日付列はTIMESTAMP型（DateTime）として取得されるため、直接キャストしてDateOnlyに変換する
            if (reader["日付"] is DateTime dateTime)
                dates.Add(DateOnly.FromDateTime(dateTime));
        }

        return dates;
    }
}
