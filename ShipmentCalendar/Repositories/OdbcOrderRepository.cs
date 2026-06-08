using ShipmentCalendar.Models;
using ShipmentCalendar.Services;
using System.Data.Odbc;

namespace ShipmentCalendar.Repositories;

/// <summary>
/// ODBC経由でVP_生産計画情報_YD / VP_受入実績情報_YD から注文データを取得するリポジトリ。
/// </summary>
public class OdbcOrderRepository : IOrderRepository
{
    private readonly AppSettings _settings;

    public OdbcOrderRepository(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task<IEnumerable<Order>> GetAllAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var rangeStart = today.AddDays(-_settings.DeliveryDatePastDays);
        var rangeEnd = today.AddDays(_settings.DeliveryDateRangeDays);

        var orders = await LoadSeisanKeikakuAsync(rangeStart, rangeEnd);
        await LoadUkeireJissekiAsync(orders);

        return orders.Values;
    }

    /// <summary>生産計画ビューから注文を取得し、日付フィルターはC#側で適用する（ドライバーの型差異を回避）</summary>
    private async Task<Dictionary<string, Order>> LoadSeisanKeikakuAsync(DateOnly rangeStart, DateOnly rangeEnd)
    {
        var orders = new Dictionary<string, Order>();

        using var conn = OdbcConnectionFactory.Create(_settings);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 製番, 品目番号, 品目名, 納期, 計画数 FROM VP_生産計画情報_YD";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
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

            _ = double.TryParse(reader["計画数"]?.ToString(), out double plannedQty);

            orders[seiban] = new Order
            {
                ProductName = reader["品目名"]?.ToString()?.Trim() ?? string.Empty,
                ItemNumber = itemNumber,
                ManufactureNumber = seiban,
                DeliveryDate = deliveryDate.Value,
                PlannedQuantity = (int)plannedQty
            };
        }

        return orders;
    }

    /// <summary>受入実績ビューから完了工程を取得して注文に仮登録する（製番リストを1000件ずつバッチ処理）</summary>
    private async Task LoadUkeireJissekiAsync(Dictionary<string, Order> orders)
    {
        if (orders.Count == 0) return;

        using var conn = OdbcConnectionFactory.Create(_settings);
        await conn.OpenAsync();

        var keys = orders.Keys.ToList();
        const int batchSize = 1000;

        for (int i = 0; i < keys.Count; i += batchSize)
        {
            var batchKeys = keys.Skip(i).Take(batchSize);
            var seibanList = string.Join(",", batchKeys.Select(s => $"'{s.Replace("'", "''")}'"));

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"SELECT 製番, 指示内容 FROM VP_受入実績情報_YD
                WHERE 製番 IN ({seibanList})
                  AND 指示内容 IS NOT NULL
                  AND 指示内容 <> '< NULL >'";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var seiban = reader["製番"]?.ToString()?.Trim() ?? string.Empty;
                var processCode = reader["指示内容"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(seiban) || string.IsNullOrEmpty(processCode)) continue;
                if (!orders.TryGetValue(seiban, out var order)) continue;

                // 完了工程を仮登録（ProcessName = 指示内容コード。BuildProcessesで変換される）
                if (!order.Processes.Any(p => p.ProcessName == processCode))
                {
                    order.Processes.Add(new OrderProcess
                    {
                        ProcessName = processCode,
                        Status = ProcessStatus.Completed
                    });
                }
            }
        }
    }

    private static DateOnly? ToDateOnly(object? value)
    {
        if (value is DateTime dt) return DateOnly.FromDateTime(dt);
        if (value is string s && DateTime.TryParse(s, out var parsed)) return DateOnly.FromDateTime(parsed);
        return null;
    }

    public Task<Order?> GetByIdAsync(int id) => Task.FromResult<Order?>(null);
    public Task AddAsync(Order order) => Task.CompletedTask;
    public Task UpdateAsync(Order order) => Task.CompletedTask;
    public Task DeleteAsync(int id) => Task.CompletedTask;
    public Task AddRangeAsync(IEnumerable<Order> orders) => Task.CompletedTask;
    public Task DeleteAllAsync() => Task.CompletedTask;
}
