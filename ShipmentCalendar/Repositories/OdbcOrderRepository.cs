using ShipmentCalendar.Models;
using ShipmentCalendar.Services;
using System.Data.Odbc;

namespace ShipmentCalendar.Repositories;

/// <summary>
/// ODBC経由でVP_生産計画情報_YD / VP_受入実績情報_YD から注文データを取得するリポジトリ。
/// </summary>
public class OdbcOrderRepository
{
    private readonly AppSettings _settings;

    public OdbcOrderRepository(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>VP_生産計画情報_YD から指定した機種コード（半製品）の品目番号・品目名を重複除外して取得する（品目番号昇順、日付範囲なし）。
    /// excludeItemNumberEqualsSeiban が true の場合、品目番号+'-00'=製番 の行が1件でもある品目番号は除外する。
    /// excludeItemNumberStartsWithM が true の場合、品目番号が'M'で始まる品目番号は除外する。</summary>
    public IEnumerable<(string ItemNumber, string ItemName)> GetSemiFinishedItemNumbersWithNames(IEnumerable<string> modelCodes, bool excludeItemNumberEqualsSeiban, bool excludeItemNumberStartsWithM)
    {
        var codeList = string.Join(",", modelCodes.Select(c => $"'{c.Replace("'", "''")}'"));
        if (string.IsNullOrEmpty(codeList)) return Enumerable.Empty<(string, string)>();

        using var conn = OdbcConnectionFactory.Create(_settings);
        conn.Open();

        var items = new List<(string ItemNumber, string ItemName)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT 品目番号, 品目名, 製番
            FROM VP_生産計画情報_YD
            WHERE 機種コード IN ({codeList})
              AND 工場番号 = ?
              AND 品目番号 IS NOT NULL
            ORDER BY 品目番号";
        cmd.Parameters.Add("@FactoryNumber", OdbcType.VarChar).Value = _settings.OdbcFactoryNumber;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var itemNumber = reader["品目番号"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(itemNumber)) continue;
            if (excludeItemNumberStartsWithM && itemNumber.StartsWith("M", StringComparison.OrdinalIgnoreCase)) continue;

            var seiban = reader["製番"]?.ToString()?.Trim() ?? string.Empty;
            if (excludeItemNumberEqualsSeiban && string.Equals(itemNumber + "-00", seiban, StringComparison.OrdinalIgnoreCase))
            {
                excluded.Add(itemNumber);
                continue;
            }

            if (!seen.Add(itemNumber)) continue;

            var itemName = reader["品目名"]?.ToString()?.Trim() ?? string.Empty;
            items.Add((itemNumber, itemName));
        }

        if (excludeItemNumberEqualsSeiban)
            items = items.Where(i => !excluded.Contains(i.ItemNumber)).ToList();

        return items;
    }

    public IEnumerable<Order> GetAll()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var rangeStart = today.AddDays(-_settings.DeliveryDatePastDays);
        var rangeEnd = today.AddDays(_settings.DeliveryDateRangeDays);

        using var conn = OdbcConnectionFactory.Create(_settings);
        conn.Open();

        var orders = LoadSeisanKeikaku(conn, rangeStart, rangeEnd);
        LoadUkeireJisseki(conn, orders);

        return orders.Values;
    }

    /// <summary>生産計画ビューから注文を取得し、日付フィルターはC#側で適用する（ドライバーの型差異を回避）</summary>
    private static Dictionary<string, Order> LoadSeisanKeikaku(OdbcConnection conn, DateOnly rangeStart, DateOnly rangeEnd)
    {
        var orders = new Dictionary<string, Order>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 製番, 品目番号, 品目名, 納期, 計画数, 機種コード FROM VP_生産計画情報_YD";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var seiban = reader["製番"]?.ToString()?.Trim() ?? string.Empty;
            var itemNumber = reader["品目番号"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(seiban) || string.IsNullOrEmpty(itemNumber)) continue;

            var deliveryDate = ToDateOnly(reader["納期"]);
            if (deliveryDate == null) continue;

            // 日付フィルター（C#側）
            if (deliveryDate.Value < rangeStart || deliveryDate.Value > rangeEnd) continue;

            // 同一製番が複数行ある場合は最初の行を採用
            if (orders.ContainsKey(seiban)) continue;

            _ = double.TryParse(reader["計画数"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double plannedQty);

            orders[seiban] = new Order
            {
                ProductName = reader["品目名"]?.ToString()?.Trim() ?? string.Empty,
                ItemNumber = itemNumber,
                ModelCode = reader["機種コード"]?.ToString()?.Trim() ?? string.Empty,
                ManufactureNumber = seiban,
                DeliveryDate = deliveryDate.Value,
                PlannedQuantity = (int)plannedQty
            };
        }

        return orders;
    }

    /// <summary>受入実績ビューから完了工程を取得して注文に仮登録する（製番リストを1000件ずつバッチ処理）</summary>
    private static void LoadUkeireJisseki(OdbcConnection conn, Dictionary<string, Order> orders)
    {
        if (orders.Count == 0) return;

        var keys = orders.Keys.ToList();
        const int batchSize = 1000;

        for (int i = 0; i < keys.Count; i += batchSize)
        {
            var batchKeys = keys.Skip(i).Take(batchSize);
            var seibanList = string.Join(",", batchKeys.Select(s => $"'{s.Replace("'", "''")}'"));

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"SELECT 製番, 指示先番号, 受入日 FROM VP_受入実績情報_YD
                WHERE 製番 IN ({seibanList})
                  AND 指示先番号 IS NOT NULL
                  AND 指示先番号 <> '< NULL >'";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var seiban = reader["製番"]?.ToString()?.Trim() ?? string.Empty;
                var processCode = reader["指示先番号"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(seiban) || string.IsNullOrEmpty(processCode)) continue;
                if (!orders.TryGetValue(seiban, out var order)) continue;

                // 完了工程を仮登録（ProcessName = 指示先番号。BuildProcessesで変換される）
                if (!order.Processes.Any(p => p.ProcessName == processCode))
                {
                    order.Processes.Add(new OrderProcess
                    {
                        ProcessName = processCode,
                        DestinationCode = processCode,
                        Status = ProcessStatus.Completed,
                        ActualDate = ToDateOnly(reader["受入日"])
                    });
                }
            }
        }
    }

    private static DateOnly? ToDateOnly(object? value)
    {
        if (value == null || value == DBNull.Value) return null;
        if (value is DateTime dt) return DateOnly.FromDateTime(dt);
        // DrSum ODBCドライバーが文字列や独自型で返す場合に対応
        var str = value.ToString();
        if (!string.IsNullOrEmpty(str) && DateTime.TryParse(str, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
            return DateOnly.FromDateTime(parsed);
        return null;
    }
}
