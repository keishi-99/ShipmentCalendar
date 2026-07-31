namespace ShipmentCalendar.Models;

/// <summary>部署の日別欠員数。基本人数(Department.Headcount)からこの日数分を差し引いた値を実働人数とする</summary>
public class DepartmentAbsence
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public DateOnly Date { get; set; }
    public int AbsentCount { get; set; }
}
