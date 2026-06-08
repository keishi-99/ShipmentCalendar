using CsvHelper;
using CsvHelper.Configuration;
using ShipmentCalendar.Models;
using System.Globalization;
using System.IO;

namespace ShipmentCalendar.Repositories;

/// <summary>
/// VP_生産計画情報_YD と VP_受入実績情報_YD を読み込んで Order を構築するリポジトリ。
/// </summary>
public class CsvOrderRepository : IOrderRepository
{
    // DrSum エクスポートの日時フォーマット
    private static readonly string[] DateFormats =
    {
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd"
    };

    private readonly string _seisanKeikakuCsvPath;
    private readonly string _ukeireJissekiCsvPath;
    private readonly int _deliveryDateRangeDays;
    private readonly int _deliveryDatePastDays;

    public CsvOrderRepository(
        string seisanKeikakuCsvPath,
        string ukeireJissekiCsvPath,
        int deliveryDateRangeDays = 90,
        int deliveryDatePastDays = 0)
    {
        _seisanKeikakuCsvPath = seisanKeikakuCsvPath;
        _ukeireJissekiCsvPath = ukeireJissekiCsvPath;
        _deliveryDateRangeDays = deliveryDateRangeDays;
        _deliveryDatePastDays = deliveryDatePastDays;
    }

    public Task<IEnumerable<Order>> GetAllAsync()
    {
        if (!File.Exists(_seisanKeikakuCsvPath))
            return Task.FromResult(Enumerable.Empty<Order>());

        var today = DateOnly.FromDateTime(DateTime.Today);
        var rangeStart = today.AddDays(-_deliveryDatePastDays);
        var rangeEnd = today.AddDays(_deliveryDateRangeDays);

        // 生産計画CSV: 製番 → Order
        var ordersBySeiban = LoadSeisanKeikakuCsv(rangeStart, rangeEnd);

        // 受入実績CSV: 製番 → 完了済み指示内容コードのセット
        var completedBySeiban = File.Exists(_ukeireJissekiCsvPath)
            ? LoadUkeireJissekiCsv()
            : new Dictionary<string, HashSet<string>>();

        // 製番で結合して完了工程を設定
        foreach (var (seiban, order) in ordersBySeiban)
        {
            if (!completedBySeiban.TryGetValue(seiban, out var completedCodes)) continue;

            // 指示内容コードを ProcessName として仮登録（BuildProcesses時に CsvColumnName とマッチング）
            order.Processes = completedCodes
                .Select(code => new OrderProcess
                {
                    ProcessName = code,
                    Status = ProcessStatus.Completed
                })
                .ToList();
        }

        return Task.FromResult<IEnumerable<Order>>(ordersBySeiban.Values);
    }

    /// <summary>生産計画CSVを読み込んで表示期間内の注文を返す（キー: 製番）</summary>
    private Dictionary<string, Order> LoadSeisanKeikakuCsv(DateOnly rangeStart, DateOnly rangeEnd)
    {
        var config = CreateConfig(_seisanKeikakuCsvPath);
        var orders = new Dictionary<string, Order>();

        using var reader = new StreamReader(_seisanKeikakuCsvPath, System.Text.Encoding.UTF8);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            // 製番を結合キーとして使用
            var seiban = GetField(csv, "製番");
            var itemNumber = GetField(csv, "品目番号");
            if (string.IsNullOrEmpty(seiban) || string.IsNullOrEmpty(itemNumber)) continue;

            var deliveryDateStr = GetField(csv, "納期");
            if (!TryParseDate(deliveryDateStr, out var deliveryDate)) continue;

            if (deliveryDate < rangeStart || deliveryDate > rangeEnd) continue;

            // 同一製番が複数行ある場合は最初の行を採用
            if (orders.ContainsKey(seiban)) continue;

            _ = double.TryParse(GetField(csv, "計画数"), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double plannedQtyD);
            int plannedQty = (int)plannedQtyD;
            orders[seiban] = new Order
            {
                ProductName = GetField(csv, "品目名"),
                ItemNumber = itemNumber,
                ManufactureNumber = seiban,
                DeliveryDate = deliveryDate,
                PlannedQuantity = plannedQty
            };
        }
        return orders;
    }

    /// <summary>受入実績CSVを読み込んで製番ごとの完了指示内容セットを返す</summary>
    private Dictionary<string, HashSet<string>> LoadUkeireJissekiCsv()
    {
        var config = CreateConfig(_ukeireJissekiCsvPath);
        var result = new Dictionary<string, HashSet<string>>();

        using var reader = new StreamReader(_ukeireJissekiCsvPath, System.Text.Encoding.UTF8);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            // 製番で生産計画の製番と結合
            var seiban = GetField(csv, "製番");
            var processCode = GetField(csv, "指示内容");

            if (string.IsNullOrEmpty(seiban) || string.IsNullOrEmpty(processCode)) continue;
            if (processCode == "< NULL >") continue;

            if (!result.ContainsKey(seiban))
                result[seiban] = new HashSet<string>();
            result[seiban].Add(processCode);
        }
        return result;
    }

    /// <summary>1行目を読んでタブ・カンマを自動判定して CsvConfiguration を返す</summary>
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

    private static bool TryParseDate(string value, out DateOnly result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        foreach (var fmt in DateFormats)
        {
            if (DateTime.TryParseExact(value.Trim(), fmt,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                result = DateOnly.FromDateTime(dt);
                return true;
            }
        }
        return false;
    }

    private static string GetField(CsvReader csv, string name)
    {
        try { return csv.GetField(name) ?? string.Empty; }
        catch { return string.Empty; }
    }

    public Task<Order?> GetByIdAsync(int id) => Task.FromResult<Order?>(null);
    public Task AddAsync(Order order) => Task.CompletedTask;
    public Task UpdateAsync(Order order) => Task.CompletedTask;
    public Task DeleteAsync(int id) => Task.CompletedTask;
    public Task AddRangeAsync(IEnumerable<Order> orders) => Task.CompletedTask;
    public Task DeleteAllAsync() => Task.CompletedTask;
}
