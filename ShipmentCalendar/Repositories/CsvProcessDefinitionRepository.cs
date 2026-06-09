using CsvHelper;
using CsvHelper.Configuration;
using ShipmentCalendar.Models;
using System.Globalization;
using System.IO;

namespace ShipmentCalendar.Repositories;

/// <summary>
/// VP_指示工程情報_YD と VP_名称情報_YD から工程定義を取得するリポジトリ。
/// ProcessDefinitionsDB の代替として使用する。
/// </summary>
public class CsvProcessDefinitionRepository : IProcessDefinitionRepository
{
    // VP_名称情報_YD における指示内容の区分番号
    private const string ShijiNaiyoKubun = "063";

    private readonly string _shijiKoteiCsvPath;
    private readonly string _meishoJohoCsvPath;

    public CsvProcessDefinitionRepository(string shijiKoteiCsvPath, string meishoJohoCsvPath)
    {
        _shijiKoteiCsvPath = shijiKoteiCsvPath;
        _meishoJohoCsvPath = meishoJohoCsvPath;
    }

    public Task<IEnumerable<ProcessDefinition>> GetAllAsync()
    {
        if (!File.Exists(_shijiKoteiCsvPath))
            return Task.FromResult(Enumerable.Empty<ProcessDefinition>());

        // 名称情報CSV: 名称番号（=指示内容コード）→ 名称名
        var nameDict = LoadNameDict();

        var definitions = new List<ProcessDefinition>();
        var config = CreateConfig(_shijiKoteiCsvPath);

        using var reader = new StreamReader(_shijiKoteiCsvPath, System.Text.Encoding.UTF8);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            var itemNumber = GetField(csv, "品目番号");
            var processCode = GetField(csv, "指示内容");
            if (string.IsNullOrEmpty(itemNumber) || string.IsNullOrEmpty(processCode)) continue;
            if (processCode == "< NULL >") continue;

            _ = int.TryParse(GetField(csv, "順序"), out int sortOrder);
            _ = int.TryParse(GetField(csv, "製造ＬＴ"), out int leadTimeDays);

            // 名称情報CSVから表示用工程名を取得。なければ指示内容コードをそのまま使用
            var processName = nameDict.TryGetValue(processCode, out var name) ? name : processCode;

            definitions.Add(new ProcessDefinition
            {
                ItemNumber = itemNumber,
                ProcessName = processName,
                CsvColumnName = processCode,     // 受入実績の指示内容と照合するコード
                LeadTimeHours = leadTimeDays * 8.0,
                SortOrder = sortOrder,
                IsVisible = true,
                WarningDaysBeforeDeadline = 0
            });
        }

        return Task.FromResult<IEnumerable<ProcessDefinition>>(definitions);
    }

    public Task<IEnumerable<ProcessDefinition>> GetByItemNumberAsync(string itemNumber)
    {
        return GetAllAsync().ContinueWith(t =>
            t.Result.Where(d => d.ItemNumber == itemNumber));
    }

    public Task<IEnumerable<string>> GetItemNumbersAsync()
    {
        return GetAllAsync().ContinueWith(t =>
            t.Result.Select(d => d.ItemNumber).Distinct());
    }

    // 書き込み系は CSV リポジトリでは未対応（DB 版を使用）
    public Task AddAsync(ProcessDefinition definition) => Task.CompletedTask;
    public Task UpdateAsync(ProcessDefinition definition) => Task.CompletedTask;
    public Task DeleteAsync(int id) => Task.CompletedTask;

    /// <summary>VP_名称情報_YD から区分番号=063 の 名称番号→名称名 辞書を構築する</summary>
    private Dictionary<string, string> LoadNameDict()
    {
        var dict = new Dictionary<string, string>();
        if (!File.Exists(_meishoJohoCsvPath)) return dict;

        var config = CreateConfig(_meishoJohoCsvPath);
        using var reader = new StreamReader(_meishoJohoCsvPath, System.Text.Encoding.UTF8);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            var kubun = GetField(csv, "区分番号");
            if (kubun != ShijiNaiyoKubun) continue;

            var bangou = GetField(csv, "名称番号");
            var mei = GetField(csv, "名称名");
            if (!string.IsNullOrEmpty(bangou) && !string.IsNullOrEmpty(mei))
                dict[bangou] = mei;
        }
        return dict;
    }

    private static CsvConfiguration CreateConfig(string filePath)
    {
        var firstLine = File.ReadLines(filePath, System.Text.Encoding.UTF8).FirstOrDefault() ?? string.Empty;
        var delimiter = firstLine.Contains('\t') ? "\t" : ",";
        return new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = delimiter,
            BadDataFound = null,
            MissingFieldFound = null
        };
    }

    private static string GetField(CsvReader csv, string name)
    {
        try { return csv.GetField(name) ?? string.Empty; }
        catch { return string.Empty; }
    }
}
