namespace ShipmentCalendar.Models;

/// <summary>機種コードマスタ（機種コードごとに「製品」/「半製品」の区分を登録する）</summary>
public class ModelCodeDefinition
{
    public int Id { get; set; }
    /// <summary>機種コード</summary>
    public string ModelCode { get; set; } = string.Empty;
    /// <summary>表示名</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>区分（"製品" または "半製品"）</summary>
    public string Category { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
