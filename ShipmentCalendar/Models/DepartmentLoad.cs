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
    /// <summary>充足率（%）＝合計必要時間÷(実働人数×1日の稼働時間)。実働人数を算出できない（基本人数未設定、または欠員が基本人数以上）場合はnull</summary>
    public double? FulfillmentPercent { get; init; }
    /// <summary>超過時間（分）＝合計必要時間−(実働人数×1日の稼働時間)。超過していない、または実働人数を算出できない場合はnull</summary>
    public double? ExcessMinutes { get; init; }
    /// <summary>この日の欠員数（0=欠員なし）</summary>
    public int AbsentCount { get; init; }
    /// <summary>このセルの集計元になった注文・工程（ドリルダウン表示用）</summary>
    public List<DepartmentLoadCellItem> Items { get; init; } = [];

    private string OvertimeSuffix => ExcessMinutes is { } excess ? $"(+{excess / 60.0:F1}h)" : string.Empty;

    private string FulfillmentText => FulfillmentPercent is { } percent
        ? AbsentCount > 0 ? $"\n{percent:F0}%{OvertimeSuffix}\n欠員{AbsentCount}人" : $"\n{percent:F0}%{OvertimeSuffix}"
        : string.Empty;

    // セル表示用（1行目：割合/超過時間、2行目：件数/時間/欠員）。Tooltipは従来通りFulfillmentTextの改行結合を使う
    public string ProcessCountText => ProcessCount == 0 ? string.Empty : $"{ProcessCount}件";
    public string TotalHoursText => ProcessCount == 0 ? string.Empty : $"{TotalMinutes / 60.0:F1}h";
    // 工程が無い日は基本人数設定があると充足率が0%になるが、実質「対象工程なし」なので表示しない
    public string FulfillmentPercentText => ProcessCount > 0 && FulfillmentPercent is { } percent ? $"{percent:F0}%" : string.Empty;
    public string OvertimeText => ProcessCount > 0 ? OvertimeSuffix : string.Empty;
    public string AbsentCellText => AbsentCount > 0 ? $"欠{AbsentCount}" : string.Empty;

    public string Tooltip => ProcessCount == 0
        ? string.Empty
        : $"{Date:M/d} 件数:{ProcessCount}　合計必要時間:{TotalMinutes / 60.0:F1}h{FulfillmentText}（クリックで明細表示）";
}

/// <summary>締切集中度セルの集計元になった注文・工程（ドリルダウン一覧の1行分）</summary>
public class DepartmentLoadCellItem
{
    public required Order Order { get; init; }
    public required OrderProcess Process { get; init; }
    /// <summary>このセルの日付（Process.DailyMinutesのキー）</summary>
    public required DateOnly Date { get; init; }
    /// <summary>このセルの日付に按分された必要時間（分）。工程全体のRequiredMinutesとは異なる場合がある</summary>
    public required double DayMinutes { get; init; }

    public string ManufactureNumber => Order.ManufactureNumber;
    public int Quantity => Order.PlannedQuantity;
    public string ProductName => Order.ProductName;
    public string ProcessName => Process.ProcessName;

    /// <summary>工程の標準時間が複数営業日に分散しているか（Process.DailyMinutesが2日以上）</summary>
    private bool IsMultiDay => Process.DailyMinutes.Count > 1;

    public string DayMinutesText => $"{DayMinutes / 60.0:F1}h";
    /// <summary>複数日にまたがる工程の場合のみ、工程全体の標準時間を表示する</summary>
    public string TotalMinutesText => IsMultiDay ? $"{Process.RequiredMinutes / 60.0:F1}h" : string.Empty;
    /// <summary>複数日にまたがる工程の場合のみ、営業日ベースで「何日中の何日目」かを表示する（休日で日付が飛んでいても日数は正しく数えられる）</summary>
    public string ProgressText => IsMultiDay
        ? $"{Process.DailyMinutes.Keys.Count(d => d <= Date)}/{Process.DailyMinutes.Count}日目"
        : string.Empty;
}

/// <summary>部署別締切集中度カレンダーの1部署分の行（同一Windowで表示する全行はCellsのインデックスを共有する）</summary>
public class DepartmentLoadRow
{
    public int DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    /// <summary>基本人数。0=未設定。編集は「部署別欠員設定」画面で行う</summary>
    public int Headcount { get; init; }
    public List<DepartmentLoadCell> Cells { get; init; } = [];

    /// <summary>部署列に表示するテキスト。基本人数が設定されていれば部署名の下に添える</summary>
    public string DepartmentDisplayText => Headcount > 0 ? $"{DepartmentName}\n{Headcount}人" : DepartmentName;
}
