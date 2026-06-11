using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Repositories;

/// <summary>
/// ODBC経由でVP_指示工程情報_YD + VP_取引先情報_YD から工程定義を取得するリポジトリ。
/// V_指示工程情報_YD（ビュー）は使用しない。
/// </summary>
public class OdbcProcessDefinitionRepository
{
    private readonly AppSettings _settings;

    public OdbcProcessDefinitionRepository(AppSettings settings)
    {
        _settings = settings;
    }

    public IEnumerable<ProcessDefinition> GetAll()
    {
        using var conn = OdbcConnectionFactory.Create(_settings);
        conn.Open();

        var definitions = new List<ProcessDefinition>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT A.品目番号 AS 品目番号, A.指示先番号 AS 指示先番号, A.順序 AS 順序, A.段取時間 AS 段取時間, A.作業時間 AS 作業時間, B.取引先名称 AS 取引先名称
            FROM VP_指示工程情報_YD A
            LEFT JOIN VP_取引先情報_YD B ON A.指示先番号 = B.取引先番号
            WHERE A.指示先番号 IS NOT NULL
              AND A.指示先番号 <> '< NULL >'";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var itemNumber = reader["品目番号"]?.ToString()?.Trim() ?? string.Empty;
            var destNumber = reader["指示先番号"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(itemNumber) || string.IsNullOrEmpty(destNumber)) continue;

            _ = int.TryParse(reader["順序"]?.ToString(), out int sortOrder);
            _ = double.TryParse(reader["段取時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double setup);
            _ = double.TryParse(reader["作業時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double work);
            var supplierName = reader["取引先名称"]?.ToString()?.Trim();
            var processName = string.IsNullOrEmpty(supplierName) ? destNumber : supplierName;

            definitions.Add(new ProcessDefinition
            {
                ItemNumber = itemNumber,
                ProcessName = processName,
                DestinationCode = destNumber,
                LeadTimeMinutes = setup + work,
                SortOrder = sortOrder,
                IsVisible = true,
                WarningDaysBeforeDeadline = 0
            });
        }

        return definitions;
    }

    public IEnumerable<ProcessDefinition> GetByItemNumber(string itemNumber)
    {
        if (string.IsNullOrEmpty(itemNumber)) return Enumerable.Empty<ProcessDefinition>();

        using var conn = OdbcConnectionFactory.Create(_settings);
        conn.Open();

        var definitions = new List<ProcessDefinition>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT A.指示先番号 AS 指示先番号, A.順序 AS 順序, A.段取時間 AS 段取時間, A.作業時間 AS 作業時間, B.取引先名称 AS 取引先名称
            FROM VP_指示工程情報_YD A
            LEFT JOIN VP_取引先情報_YD B ON A.指示先番号 = B.取引先番号
            WHERE A.品目番号 = ?
              AND A.指示先番号 IS NOT NULL
              AND A.指示先番号 <> '< NULL >'";
        cmd.Parameters.Add("@ItemNumber", System.Data.Odbc.OdbcType.VarChar).Value = itemNumber;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var destNumber = reader["指示先番号"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(destNumber)) continue;

            _ = int.TryParse(reader["順序"]?.ToString(), out int sortOrder);
            _ = double.TryParse(reader["段取時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double setup);
            _ = double.TryParse(reader["作業時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double work);
            var supplierName = reader["取引先名称"]?.ToString()?.Trim();
            var processName = string.IsNullOrEmpty(supplierName) ? destNumber : supplierName;

            definitions.Add(new ProcessDefinition
            {
                ItemNumber = itemNumber,
                ProcessName = processName,
                DestinationCode = destNumber,
                LeadTimeMinutes = setup + work,
                SortOrder = sortOrder,
                IsVisible = true,
                WarningDaysBeforeDeadline = 0
            });
        }

        return definitions;
    }

    public IEnumerable<string> GetItemNumbers()
    {
        using var conn = OdbcConnectionFactory.Create(_settings);
        conn.Open();

        var itemNumbers = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT DISTINCT 品目番号
            FROM VP_指示工程情報_YD
            WHERE 品目番号 IS NOT NULL
            ORDER BY 品目番号";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var item = reader["品目番号"]?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(item))
                itemNumbers.Add(item);
        }

        return itemNumbers;
    }
}
