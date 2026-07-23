namespace ShipmentCalendar.Models;

/// <summary>部署別締切集中度カレンダーの1日分のセル</summary>
public class DepartmentLoadCell
{
    public DateOnly Date { get; init; }
    /// <summary>この日がDueDate（完了必須日）になっている未完了工程の件数</summary>
    public int ProcessCount { get; init; }
    /// <summary>上記工程の必要時間（分）合計</summary>
    public double TotalMinutes { get; init; }
    public CongestionLevel Level { get; init; }
    /// <summary>このセルの集計元になった注文・工程（ドリルダウン表示用）</summary>
    public List<DepartmentLoadCellItem> Items { get; init; } = [];

    public string DisplayText => ProcessCount == 0 ? string.Empty : $"{ProcessCount}件\n{TotalMinutes / 60.0:F1}h";
    public string Tooltip => ProcessCount == 0
        ? string.Empty
        : $"{Date:M/d} 件数:{ProcessCount}　合計必要時間:{TotalMinutes / 60.0:F1}h（ダブルクリックで詳細）";
}

/// <summary>締切集中度セルの集計元になった注文・工程（ドリルダウン一覧の1行分）</summary>
public class DepartmentLoadCellItem
{
    public required Order Order { get; init; }
    public required OrderProcess Process { get; init; }

    public string ManufactureNumber => Order.ManufactureNumber;
    public string ProductName => Order.ProductName;
    public string ProcessName => Process.ProcessName;
    public string RequiredTimeText => $"{Process.RequiredMinutes / 60.0:F1}h";
}

/// <summary>部署別締切集中度カレンダーの1部署分の行（同一Windowで表示する全行はCellsのインデックスを共有する）</summary>
public class DepartmentLoadRow
{
    public string DepartmentName { get; init; } = string.Empty;
    public List<DepartmentLoadCell> Cells { get; init; } = [];
}
