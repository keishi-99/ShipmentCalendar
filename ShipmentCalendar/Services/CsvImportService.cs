using CsvHelper;
using CsvHelper.Configuration;
using ShipmentCalendar.Models;
using System.Globalization;
using System.IO;

namespace ShipmentCalendar.Services;

/// <summary>DrSumエクスポートCSVを読み込み注文リストに変換する</summary>
public class CsvImportService
{
    private static readonly string[] DateFormats = { "yyyy/MM/dd", "yyyy-MM-dd", "MM/dd/yyyy" };

    /// <summary>CSVを読み込み、工程定義と照合して完了日からステータスを設定する</summary>
    public List<Order> Import(string filePath, IEnumerable<ProcessDefinition>? allDefinitions = null)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null
        };

        using var reader = new StreamReader(filePath, System.Text.Encoding.UTF8);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        // 製品ごとの工程定義をCSV列名でインデックス化
        var definitionsByProduct = allDefinitions?
            .Where(d => !string.IsNullOrEmpty(d.CsvColumnName))
            .GroupBy(d => d.ItemNumber)
            .ToDictionary(g => g.Key, g => g.ToList())
            ?? new Dictionary<string, List<ProcessDefinition>>();

        var orders = new List<Order>();

        while (csv.Read())
        {
            var productName = GetField(csv, "製品名", "ProductName");
            var deliveryDateStr = GetField(csv, "納期", "DeliveryDate");

            if (!DateOnly.TryParseExact(deliveryDateStr, DateFormats,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var deliveryDate))
                continue;

            var order = new Order
            {
                ProductName = productName,
                ManufactureNumber = GetField(csv, "製造番号", "ManufactureNumber"),
                OrderNumber = GetField(csv, "注文番号", "OrderNumber"),
                DeliveryDate = deliveryDate
            };

            // 工程定義のCSV列名で完了日を取得してステータスに仮設定
            if (definitionsByProduct.TryGetValue(productName, out var defs))
            {
                foreach (var def in defs)
                {
                    if (!headers.Contains(def.CsvColumnName)) continue;

                    var completionDateStr = csv.GetField(def.CsvColumnName) ?? string.Empty;
                    var isCompleted = !string.IsNullOrWhiteSpace(completionDateStr)
                        && DateOnly.TryParseExact(completionDateStr, DateFormats,
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

                    order.Processes.Add(new OrderProcess
                    {
                        ProcessName = def.ProcessName,
                        Status = isCompleted ? ProcessStatus.Completed : ProcessStatus.NotStarted,
                        SortOrder = def.SortOrder
                    });
                }
            }

            orders.Add(order);
        }

        return orders;
    }

    private static string GetField(CsvReader csv, params string[] names)
    {
        foreach (var name in names)
        {
            try { return csv.GetField(name) ?? string.Empty; }
            catch { }
        }
        return string.Empty;
    }
}
