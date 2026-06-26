using ShipmentCalendar.Models;
using ShipmentCalendar.Services;
using System.Data.Odbc;

namespace ShipmentCalendar.Repositories;

/// <summary>
/// ODBC経由でVP_生産計画情報_YD / VP_受入実績情報_YD から注文データを取得するリポジトリ。
/// </summary>
public class OdbcOrderRepository(AppSettings settings) {
    /// <summary>VP_生産計画情報_YD から指定した機種コード（半製品）の品目番号・品目名を重複除外して取得する（品目番号昇順、日付範囲なし）。
    /// excludeItemNumberEqualsSeiban が true の場合、品目番号+'-00'=製番 の行が1件でもある品目番号は除外する。
    /// excludeItemNumberStartsWithM が true の場合、品目番号が'M'で始まる品目番号は除外する。</summary>
    public IEnumerable<(string ItemNumber, string ItemName)> GetSemiFinishedItemNumbersWithNames(IEnumerable<string> modelCodes, bool excludeItemNumberEqualsSeiban, bool excludeItemNumberStartsWithM) {
        var codeList = string.Join(",", modelCodes.Select(c => $"'{c.Replace("'", "''")}'"));
        if (string.IsNullOrEmpty(codeList)) return [];

        using var conn = OdbcConnectionFactory.Create(settings);
        conn.Open();

        List<(string ItemNumber, string ItemName)> items = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT 品目番号, 品目名, 製番
            FROM VP_生産計画情報_YD
            WHERE 機種コード IN ({codeList})
              AND 工場番号 = ?
              AND 品目番号 IS NOT NULL
            ORDER BY 品目番号";
        cmd.Parameters.Add("@FactoryNumber", OdbcType.VarChar).Value = settings.OdbcFactoryNumber;

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            var itemNumber = reader["品目番号"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(itemNumber)) continue;
            if (excludeItemNumberStartsWithM && itemNumber.StartsWith("M", StringComparison.OrdinalIgnoreCase)) continue;

            var seiban = reader["製番"]?.ToString()?.Trim() ?? string.Empty;
            if (excludeItemNumberEqualsSeiban && string.Equals(itemNumber + "-00", seiban, StringComparison.OrdinalIgnoreCase)) {
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

    public IEnumerable<Order> GetAll() {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var rangeStart = today.AddDays(-settings.DeliveryDatePastDays);
        var rangeEnd = today.AddDays(settings.DeliveryDateRangeDays);

        using var conn = OdbcConnectionFactory.Create(settings);
        conn.Open();

        var orders = LoadSeisanKeikaku(conn, rangeStart, rangeEnd);
        LoadUkeireJisseki(conn, orders);

        return orders.Values;
    }

    /// <summary>生産計画ビューに日付フィルター無しで1件以上データが存在するか確認する。
    /// GetAll()が0件だった場合に、取得範囲の絞り込みによる正常な0件なのか、
    /// ERPの一時的な空読み（バックアップ処理等）なのかを区別するために使う</summary>
    public bool HasAnySeisanKeikakuRecord() {
        using var conn = OdbcConnectionFactory.Create(settings);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 製番 FROM VP_生産計画情報_YD";

        using var reader = cmd.ExecuteReader();
        return reader.Read();
    }

    /// <summary>生産計画ビューから注文を取得する。日付フィルターはSQL側で適用（文字列形式でドライバーの型差異を回避）</summary>
    private static Dictionary<string, Order> LoadSeisanKeikaku(OdbcConnection conn, DateOnly rangeStart, DateOnly rangeEnd) {
        Dictionary<string, Order> orders = [];

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT 製番, 品目番号, 品目名, 納期, 計画数, 機種コード FROM VP_生産計画情報_YD WHERE 納期 >= '{rangeStart:yyyy-MM-dd}' AND 納期 <= '{rangeEnd:yyyy-MM-dd}'";

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            var seiban = reader["製番"]?.ToString()?.Trim() ?? string.Empty;
            var itemNumber = reader["品目番号"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(seiban) || string.IsNullOrEmpty(itemNumber)) continue;

            var deliveryDate = ToDateOnly(reader["納期"]);
            if (deliveryDate == null) continue;

            // 同一製番が複数行ある場合は最初の行を採用
            if (orders.ContainsKey(seiban)) continue;

            _ = double.TryParse(reader["計画数"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double plannedQty);

            orders[seiban] = new Order {
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
    private static void LoadUkeireJisseki(OdbcConnection conn, Dictionary<string, Order> orders) {
        if (orders.Count == 0) return;

        var keys = orders.Keys.ToList();
        const int BatchSize = 1000;

        for (int i = 0; i < keys.Count; i += BatchSize) {
            var batchKeys = keys.Skip(i).Take(BatchSize);
            var seibanList = string.Join(",", batchKeys.Select(s => $"'{s.Replace("'", "''")}'"));

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"SELECT t1.製番 AS 製番, t1.指示先番号 AS 指示先番号, t1.受入日 AS 受入日, t1.作業時間 AS 作業時間, t2.担当者名 AS 担当者名
                FROM VP_受入実績情報_YD t1
                LEFT JOIN VP_ユーザ情報_YD t2 ON t1.手配担当者 = t2.ユーザID
                WHERE t1.製番 IN ({seibanList})
                  AND t1.指示先番号 IS NOT NULL
                  AND t1.指示先番号 <> '< NULL >'";

            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                var seiban = reader["製番"]?.ToString()?.Trim() ?? string.Empty;
                var processCode = reader["指示先番号"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(seiban) || string.IsNullOrEmpty(processCode)) continue;
                if (!orders.TryGetValue(seiban, out var order)) continue;

                _ = double.TryParse(reader["作業時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double workMinutes);

                // 完了工程を仮登録（ProcessName = 指示先番号。BuildProcessesで変換される）
                var process = order.Processes.FirstOrDefault(p => p.ProcessName == processCode);
                if (process == null) {
                    order.Processes.Add(new OrderProcess {
                        ProcessName = processCode,
                        DestinationCode = processCode,
                        Status = ProcessStatus.Completed,
                        ActualDate = ToDateOnly(reader["受入日"]),
                        WorkerName = reader["担当者名"]?.ToString()?.Trim() ?? string.Empty,
                        ActualWorkMinutes = workMinutes
                    });
                } else {
                    // 同一工程に複数の受入実績がある場合は作業時間を合計する
                    process.ActualWorkMinutes += workMinutes;
                }
            }
        }
    }

    private static DateOnly? ToDateOnly(object? value) {
        if (value == null || value == DBNull.Value) return null;
        if (value is DateTime dt) return DateOnly.FromDateTime(dt);
        // DrSum ODBCドライバーが文字列や独自型で返す場合に対応
        var str = value.ToString();
        if (!string.IsNullOrEmpty(str) && DateTime.TryParse(str, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
            return DateOnly.FromDateTime(parsed);
        return null;
    }
}
