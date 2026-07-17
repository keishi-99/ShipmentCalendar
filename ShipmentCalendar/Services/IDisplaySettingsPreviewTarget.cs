namespace ShipmentCalendar.Services;

/// <summary>表示設定ダイアログからのリアルタイムプレビューを受け取る側（MainWindowが実装する）</summary>
public interface IDisplaySettingsPreviewTarget {
    void PreviewRowHeight(double height);
    void PreviewFontSizes(double fixedSize, double processBarSize, double processColumnSize = 0);

    /// <summary>プレビュー状態を終了し、保存・キャンセルに関わらず最終的な設定値をグリッドに反映する</summary>
    void EndPreview();
}
