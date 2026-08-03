namespace ShipmentCalendar.Models;

/// <summary>工程別ボトルネック分析の集計結果行（1品目×1工程分）</summary>
public class ProcessBottleneckRow {
    public string ItemNumber { get; set; } = string.Empty;
    /// <summary>品目名（VP_生産計画情報_YD.品目名）</summary>
    public string ItemName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    /// <summary>期間内に完了実績があった件数</summary>
    public int CompletedCount { get; set; }
    /// <summary>標準時間（RequiredMinutes）を超えて実績時間がかかった件数</summary>
    public int OverStandardCount { get; set; }
    /// <summary>標準時間超過の平均超過分数（超過した件のみで平均。超過が無い場合は0）</summary>
    public double AvgOverMinutes { get; set; }
    /// <summary>標準時間（RequiredMinutes）に対して実績時間が短かった件数</summary>
    public int UnderStandardCount { get; set; }
    /// <summary>標準時間未達の平均短縮分数（未達だった件のみで平均。未達が無い場合は0）</summary>
    public double AvgUnderMinutes { get; set; }
    /// <summary>この行の集計元になった個別注文（ドリルダウン表示用）</summary>
    public List<ProcessBottleneckItem> Items { get; set; } = [];

    public double OverStandardRate => CompletedCount > 0 ? (double)OverStandardCount / CompletedCount : 0;
    public double UnderStandardRate => CompletedCount > 0 ? (double)UnderStandardCount / CompletedCount : 0;

    public string OverStandardRateText => $"{OverStandardRate * 100:F0}%";
    public string AvgOverMinutesText => OverStandardCount > 0 ? $"{AvgOverMinutes:F1}分" : "-";
    public string UnderStandardRateText => $"{UnderStandardRate * 100:F0}%";
    public string AvgUnderMinutesText => UnderStandardCount > 0 ? $"{AvgUnderMinutes:F1}分" : "-";
}

/// <summary>工程別ボトルネック分析セルの集計元になった個別注文（ドリルダウン一覧の1行分）</summary>
public class ProcessBottleneckItem {
    public string Seiban { get; init; } = string.Empty;
    public double RequiredMinutes { get; init; }
    public double ActualWorkMinutes { get; init; }

    /// <summary>実績時間と標準時間の差（分）。プラスは超過、マイナスは未達を表す</summary>
    public double OverMinutes => ActualWorkMinutes - RequiredMinutes;
    public string RequiredMinutesText => $"{RequiredMinutes:F1}分";
    public string ActualWorkMinutesText => $"{ActualWorkMinutes:F1}分";
    public string OverMinutesText => RequiredMinutes > 0 ? (OverMinutes >= 0 ? $"+{OverMinutes:F1}分" : $"{OverMinutes:F1}分") : "-";
}
