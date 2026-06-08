namespace ShipmentCalendar.Models;

/// <summary>アプリ設定（JSONファイルで永続化）</summary>
public class AppSettings
{
    /// <summary>生産計画CSVファイルパス（VP_生産計画情報_YD）</summary>
    public string SeisanKeikakuCsvPath { get; set; } = string.Empty;
    /// <summary>受入実績CSVファイルパス（VP_受入実績情報_YD）</summary>
    public string UkeireJissekiCsvPath { get; set; } = string.Empty;
    /// <summary>指示工程CSVファイルパス（VP_指示工程情報_YD）</summary>
    public string ShijiKoteiCsvPath { get; set; } = string.Empty;
    /// <summary>名称情報CSVファイルパス（VP_名称情報_YD）</summary>
    public string MeishoJohoCsvPath { get; set; } = string.Empty;
    /// <summary>自動更新間隔（分）。0=自動更新なし</summary>
    public int AutoRefreshMinutes { get; set; } = 5;
    /// <summary>表示する納期の範囲（今日から何日先まで）</summary>
    public int DeliveryDateRangeDays { get; set; } = 90;
    /// <summary>表示する納期の範囲（今日から何日前まで）</summary>
    public int DeliveryDatePastDays { get; set; } = 0;
}
