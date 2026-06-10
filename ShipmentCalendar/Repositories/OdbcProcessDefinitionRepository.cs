using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Repositories;

/// <summary>
/// ODBC経由でVP_指示工程情報_YD + VP_取引先情報_YD から工程定義を取得するリポジトリ。
/// V_指示工程情報_YD（ビュー）は使用しない。
/// </summary>
public class OdbcProcessDefinitionRepository : IProcessDefinitionRepository
{
    private readonly AppSettings _settings;

    public OdbcProcessDefinitionRepository(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task<IEnumerable<ProcessDefinition>> GetAllAsync()
    {
        using var conn = OdbcConnectionFactory.Create(_settings);
        await conn.OpenAsync();

        // 指示先番号→取引先名称の辞書を先に構築
        var nameDict = await LoadNameDictAsync(conn);

        var definitions = new List<ProcessDefinition>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT 品目番号, 指示内容, 指示先番号, 順序, 段取時間, 作業時間
            FROM VP_指示工程情報_YD
            WHERE 指示内容 IS NOT NULL
              AND 指示内容 <> '< NULL >'";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var itemNumber = reader["品目番号"]?.ToString()?.Trim() ?? string.Empty;
            var processCode = reader["指示内容"]?.ToString()?.Trim() ?? string.Empty;
            var destNumber = reader["指示先番号"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(itemNumber) || string.IsNullOrEmpty(processCode)) continue;

            _ = int.TryParse(reader["順序"]?.ToString(), out int sortOrder);
            _ = double.TryParse(reader["段取時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double setup);
            _ = double.TryParse(reader["作業時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double work);
            var processName = nameDict.TryGetValue(destNumber, out var n) ? n : processCode;

            definitions.Add(new ProcessDefinition
            {
                ItemNumber = itemNumber,
                ProcessName = processName,
                CsvColumnName = destNumber,
                LeadTimeMinutes = setup + work,
                SortOrder = sortOrder,
                IsVisible = true,
                WarningDaysBeforeDeadline = 0
            });
        }

        return definitions;
    }

    public async Task<IEnumerable<ProcessDefinition>> GetByItemNumberAsync(string itemNumber)
    {
        if (string.IsNullOrEmpty(itemNumber)) return Enumerable.Empty<ProcessDefinition>();

        using var conn = OdbcConnectionFactory.Create(_settings);
        await conn.OpenAsync();

        var nameDict = await LoadNameDictAsync(conn);

        var definitions = new List<ProcessDefinition>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT 指示内容, 指示先番号, 順序, 段取時間, 作業時間
            FROM VP_指示工程情報_YD
            WHERE 品目番号 = ?
              AND 指示内容 IS NOT NULL
              AND 指示内容 <> '< NULL >'";
        cmd.Parameters.Add("@ItemNumber", System.Data.Odbc.OdbcType.VarChar).Value = itemNumber;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var processCode = reader["指示内容"]?.ToString()?.Trim() ?? string.Empty;
            var destNumber = reader["指示先番号"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(processCode)) continue;

            _ = int.TryParse(reader["順序"]?.ToString(), out int sortOrder);
            _ = double.TryParse(reader["段取時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double setup);
            _ = double.TryParse(reader["作業時間"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double work);
            var processName = nameDict.TryGetValue(destNumber, out var n) ? n : processCode;

            definitions.Add(new ProcessDefinition
            {
                ItemNumber = itemNumber,
                ProcessName = processName,
                CsvColumnName = destNumber,
                LeadTimeMinutes = setup + work,
                SortOrder = sortOrder,
                IsVisible = true,
                WarningDaysBeforeDeadline = 0
            });
        }

        return definitions;
    }

    public async Task<IEnumerable<string>> GetItemNumbersAsync()
    {
        using var conn = OdbcConnectionFactory.Create(_settings);
        await conn.OpenAsync();

        var itemNumbers = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT DISTINCT 品目番号
            FROM VP_指示工程情報_YD
            WHERE 品目番号 IS NOT NULL
            ORDER BY 品目番号";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var item = reader["品目番号"]?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(item))
                itemNumbers.Add(item);
        }

        return itemNumbers;
    }

    /// <summary>VP_取引先情報_YD から 取引先番号（=指示先番号）→取引先名称 辞書を構築する</summary>
    private async Task<Dictionary<string, string>> LoadNameDictAsync(System.Data.Odbc.OdbcConnection conn)
    {
        var dict = new Dictionary<string, string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT 取引先番号, 取引先名称
            FROM VP_取引先情報_YD";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var code = reader["取引先番号"]?.ToString()?.Trim() ?? string.Empty;
            var name = reader["取引先名称"]?.ToString()?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(name))
                dict[code] = name;
        }
        return dict;
    }

    public Task AddAsync(ProcessDefinition definition) => Task.CompletedTask;
    public Task UpdateAsync(ProcessDefinition definition) => Task.CompletedTask;
    public Task DeleteAsync(int id) => Task.CompletedTask;
}
