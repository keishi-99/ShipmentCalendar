using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Repositories;

/// <summary>
/// ODBC経由でVP_指示工程情報_YD + VP_取引先情報_YD から工程定義を取得するリポジトリ。
/// V_指示工程情報_YD（ビュー）は使用しない。
/// </summary>
public class OdbcProcessDefinitionRepository(AppSettings settings) {
    public IEnumerable<ProcessDefinition> GetAll() {
        using var conn = OdbcConnectionFactory.Create(settings);
        conn.Open();

        List<ProcessDefinition> definitions = [];
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT A.品目番号 AS 品目番号, A.指示先番号 AS 指示先番号, A.順序 AS 順序, A.段取時間 AS 段取時間, A.作業時間 AS 作業時間, B.取引先名称 AS 取引先名称
            FROM VP_指示工程情報_YD A
            LEFT JOIN VP_取引先情報_YD B ON A.指示先番号 = B.取引先番号
            WHERE A.指示先番号 IS NOT NULL
              AND A.指示先番号 <> '< NULL >'";

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            var itemNumber = reader["品目番号"]?.ToString()?.Trim() ?? string.Empty;
            var destNumber = reader["指示先番号"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(itemNumber) || string.IsNullOrEmpty(destNumber)) continue;

            _ = int.TryParse(reader["順序"]?.ToString(), out int sortOrder);
            _ = double.TryParse(reader["段取時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double setup);
            _ = double.TryParse(reader["作業時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double work);
            var supplierName = reader["取引先名称"]?.ToString()?.Trim();
            var processName = string.IsNullOrEmpty(supplierName) ? destNumber : supplierName;

            definitions.Add(new ProcessDefinition {
                ItemNumber = itemNumber,
                ProcessName = processName,
                DestinationCode = destNumber,
                SetupTimeMinutes = setup,
                WorkTimeMinutes = work,
                SortOrder = sortOrder,
                IsVisible = true,
                WarningDaysBeforeDeadline = 0
            });
        }

        return definitions;
    }

    public IEnumerable<ProcessDefinition> GetByItemNumber(string itemNumber) {
        if (string.IsNullOrEmpty(itemNumber)) return [];

        using var conn = OdbcConnectionFactory.Create(settings);
        conn.Open();

        List<ProcessDefinition> definitions = [];
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT A.指示先番号 AS 指示先番号, A.順序 AS 順序, A.段取時間 AS 段取時間, A.作業時間 AS 作業時間, B.取引先名称 AS 取引先名称
            FROM VP_指示工程情報_YD A
            LEFT JOIN VP_取引先情報_YD B ON A.指示先番号 = B.取引先番号
            WHERE A.品目番号 = ?
              AND A.指示先番号 IS NOT NULL
              AND A.指示先番号 <> '< NULL >'";
        cmd.Parameters.Add("@ItemNumber", System.Data.Odbc.OdbcType.VarChar).Value = itemNumber;

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            var destNumber = reader["指示先番号"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(destNumber)) continue;

            _ = int.TryParse(reader["順序"]?.ToString(), out int sortOrder);
            _ = double.TryParse(reader["段取時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double setup);
            _ = double.TryParse(reader["作業時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double work);
            var supplierName = reader["取引先名称"]?.ToString()?.Trim();
            var processName = string.IsNullOrEmpty(supplierName) ? destNumber : supplierName;

            definitions.Add(new ProcessDefinition {
                ItemNumber = itemNumber,
                ProcessName = processName,
                DestinationCode = destNumber,
                SetupTimeMinutes = setup,
                WorkTimeMinutes = work,
                SortOrder = sortOrder,
                IsVisible = true,
                WarningDaysBeforeDeadline = 0
            });
        }

        return definitions;
    }

    /// <summary>複数の品目番号の工程定義を1回のODBC接続でまとめて取得する（品目番号 → 工程定義一覧）</summary>
    public IDictionary<string, List<ProcessDefinition>> GetByItemNumbers(IEnumerable<string> itemNumbers) {
        var items = itemNumbers.Where(i => !string.IsNullOrEmpty(i)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = items.ToDictionary(i => i, _ => new List<ProcessDefinition>(), StringComparer.OrdinalIgnoreCase);
        if (items.Count == 0) return result;

        using var conn = OdbcConnectionFactory.Create(settings);
        conn.Open();

        // IN句のパラメータ数がODBCドライバの上限を超えないよう、品目番号をバッチに分けてクエリを実行する
        const int BatchSize = 500;
        for (int i = 0; i < items.Count; i += BatchSize) {
            var batch = items.Skip(i).Take(BatchSize).ToList();

            using var cmd = conn.CreateCommand();
            var placeholders = string.Join(",", batch.Select(_ => "?"));
            cmd.CommandText = $@"SELECT A.品目番号 AS 品目番号, A.指示先番号 AS 指示先番号, A.順序 AS 順序, A.段取時間 AS 段取時間, A.作業時間 AS 作業時間, B.取引先名称 AS 取引先名称
                FROM VP_指示工程情報_YD A
                LEFT JOIN VP_取引先情報_YD B ON A.指示先番号 = B.取引先番号
                WHERE A.品目番号 IN ({placeholders})
                  AND A.指示先番号 IS NOT NULL
                  AND A.指示先番号 <> '< NULL >'";
            foreach (var item in batch)
                cmd.Parameters.Add("@ItemNumber", System.Data.Odbc.OdbcType.VarChar).Value = item;

            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                var itemNumber = reader["品目番号"]?.ToString()?.Trim() ?? string.Empty;
                var destNumber = reader["指示先番号"]?.ToString()?.Trim() ?? string.Empty;
                if (!result.ContainsKey(itemNumber) || string.IsNullOrEmpty(destNumber)) continue;

                _ = int.TryParse(reader["順序"]?.ToString(), out int sortOrder);
                _ = double.TryParse(reader["段取時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double setup);
                _ = double.TryParse(reader["作業時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double work);
                var supplierName = reader["取引先名称"]?.ToString()?.Trim();
                var processName = string.IsNullOrEmpty(supplierName) ? destNumber : supplierName;

                result[itemNumber].Add(new ProcessDefinition {
                    ItemNumber = itemNumber,
                    ProcessName = processName,
                    DestinationCode = destNumber,
                    SetupTimeMinutes = setup,
                    WorkTimeMinutes = work,
                    SortOrder = sortOrder,
                    IsVisible = true,
                    WarningDaysBeforeDeadline = 0
                });
            }
        }

        return result;
    }

    public IEnumerable<string> GetItemNumbers() {
        using var conn = OdbcConnectionFactory.Create(settings);
        conn.Open();

        List<string> itemNumbers = [];
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT DISTINCT 品目番号
            FROM VP_指示工程情報_YD
            WHERE 品目番号 IS NOT NULL
            ORDER BY 品目番号";

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            var item = reader["品目番号"]?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(item))
                itemNumbers.Add(item);
        }

        return itemNumbers;
    }
}
