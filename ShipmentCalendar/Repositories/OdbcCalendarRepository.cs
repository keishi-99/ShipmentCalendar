using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Repositories;

/// <summary>ODBC経由でVP_カレンダ情報_YDから休日（稼働区分='01'）を取得するリポジトリ</summary>
public class OdbcCalendarRepository
{
    private readonly AppSettings _settings;

    public OdbcCalendarRepository(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>指定年・工場番号の休日（稼働区分='01'）の日付一覧を取得する</summary>
    public async Task<IEnumerable<DateOnly>> GetHolidaysAsync(int year)
    {
        using var conn = OdbcConnectionFactory.Create(_settings);
        await conn.OpenAsync();

        var dates = new List<DateOnly>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT 日付 FROM VP_カレンダ情報_YD
            WHERE 工場番号 = ?
              AND 稼働区分 = '01'
              AND 日付_数値 BETWEEN ? AND ?";
        cmd.Parameters.Add("@FactoryNumber", System.Data.Odbc.OdbcType.VarChar).Value = _settings.OdbcFactoryNumber;
        cmd.Parameters.Add("@From", System.Data.Odbc.OdbcType.Int).Value = year * 10000 + 0101;
        cmd.Parameters.Add("@To", System.Data.Odbc.OdbcType.Int).Value = year * 10000 + 1231;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // 日付列はTIMESTAMP型で時刻が付与されるため、DateOnly.TryParseではなくDateTime.TryParse経由で変換する
            if (DateTime.TryParse(reader["日付"]?.ToString(), out var dateTime))
                dates.Add(DateOnly.FromDateTime(dateTime));
        }

        return dates;
    }
}
