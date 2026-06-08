using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Repositories;

/// <summary>
/// ODBC経由でV_指示工程情報_YD / VP_名称情報_YD から工程定義を取得するリポジトリ。
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

        // 指示内容名称はビューに含まれるため VP_名称情報_YD への別クエリは不要
        var definitions = new List<ProcessDefinition>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT 品目番号, 指示内容, 指示内容名称, 順序
            FROM V_指示工程情報_YD
            WHERE 指示内容 IS NOT NULL
              AND 指示内容 <> '< NULL >'";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var itemNumber = reader["品目番号"]?.ToString()?.Trim() ?? string.Empty;
            var processCode = reader["指示内容"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(itemNumber) || string.IsNullOrEmpty(processCode)) continue;

            _ = int.TryParse(reader["順序"]?.ToString(), out int sortOrder);
            var processName = reader["指示内容名称"]?.ToString()?.Trim();
            if (string.IsNullOrEmpty(processName)) processName = processCode;

            definitions.Add(new ProcessDefinition
            {
                ItemNumber = itemNumber,
                ProcessName = processName,
                CsvColumnName = processCode,
                LeadTimeDays = 0,
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

        var definitions = new List<ProcessDefinition>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT 品目番号, 指示内容, 指示内容名称, 順序
            FROM V_指示工程情報_YD
            WHERE 品目番号 = ?
              AND 指示内容 IS NOT NULL
              AND 指示内容 <> '< NULL >'";
        cmd.Parameters.Add("@ItemNumber", System.Data.Odbc.OdbcType.VarChar).Value = itemNumber;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var processCode = reader["指示内容"]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(processCode)) continue;

            _ = int.TryParse(reader["順序"]?.ToString(), out int sortOrder);
            var processName = reader["指示内容名称"]?.ToString()?.Trim();
            if (string.IsNullOrEmpty(processName)) processName = processCode;

            definitions.Add(new ProcessDefinition
            {
                ItemNumber = itemNumber,
                ProcessName = processName,
                CsvColumnName = processCode,
                LeadTimeDays = 0,
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
            FROM V_指示工程情報_YD
            WHERE 品目番号 IS NOT NULL
            ORDER BY 品目番号";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var itemNumber = reader["品目番号"]?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(itemNumber))
                itemNumbers.Add(itemNumber);
        }

        return itemNumbers;
    }

public Task AddAsync(ProcessDefinition definition) => Task.CompletedTask;
    public Task UpdateAsync(ProcessDefinition definition) => Task.CompletedTask;
    public Task DeleteAsync(int id) => Task.CompletedTask;
}
