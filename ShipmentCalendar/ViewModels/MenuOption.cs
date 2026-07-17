namespace ShipmentCalendar.ViewModels;

/// <summary>コンボボックスの選択肢1件（表示ラベルと実際の値の組）を表す</summary>
public record MenuOption<T>(string Label, T Value);
