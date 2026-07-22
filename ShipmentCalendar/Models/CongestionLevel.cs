namespace ShipmentCalendar.Models;

/// <summary>部署別・日別の締切集中度。部署のキャパシティは流動的で固定値を持てないため、
/// 「稼働率」ではなくその日にDueDateを迎える工程の件数・時間の集中具合を相対的に表す</summary>
public enum CongestionLevel
{
    Normal,       // 通常
    Caution,      // やや集中
    Concentrated  // 集中（要注意）
}
