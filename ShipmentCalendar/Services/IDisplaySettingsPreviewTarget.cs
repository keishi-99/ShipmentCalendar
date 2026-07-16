namespace ShipmentCalendar.Services;

/// <summary>表示設定ダイアログからのリアルタイムプレビューを受け取る側（MainWindowが実装する）</summary>
public interface IDisplaySettingsPreviewTarget {
    void PreviewRowHeight(double height);
    void PreviewFontSizes(double fixedSize, double processBarSize, double processColumnSize = 0);
}
