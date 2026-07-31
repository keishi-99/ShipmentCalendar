namespace ShipmentCalendar.Models;

/// <summary>部署別欠員設定ウィンドウの1部署分の行（同一Windowで表示する全行はCellsのインデックスを共有する）</summary>
public class DepartmentAbsenceSettingRow
{
    public int DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public int Headcount { get; set; }
    public List<DepartmentAbsenceSettingCell> Cells { get; init; } = [];
}

/// <summary>部署別欠員設定ウィンドウの1日分のセル</summary>
public class DepartmentAbsenceSettingCell
{
    public DateOnly Date { get; init; }
    public int AbsentCount { get; set; }
}
