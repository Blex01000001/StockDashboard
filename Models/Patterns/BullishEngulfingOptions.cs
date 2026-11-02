namespace StockDashboard.Models.Patterns
{
    /// <summary>
    /// 看漲吞沒的判斷參數 (頁面上可微調)。預設值為與使用者討論後的版本，修改預設前先問使用者。
    /// 百分比欄位一律以「%」為單位，例如 0.5 代表 0.5%。
    /// </summary>
    public class BullishEngulfingOptions
    {
        // 前一天黑 K 的實體至少佔開盤價幾 %（排除十字線這類實體太小的 K 棒）
        public double MinPrevBodyPct { get; set; } = 0.5;

        // 當天紅 K 實體至少是前一天黑 K 實體的幾倍
        public double MinBodyRatio { get; set; } = 1.0;

        // 吞沒的容許誤差 %：開盤 / 收盤與前一天實體邊界差這麼多以內也算吞沒
        public double TolerancePct { get; set; } = 0;

        // true：當天最高 / 最低也要包住前一天的最高 / 最低（連影線一起吞沒）
        public bool IncludeShadows { get; set; } = false;

        // 是否要求型態出現前先下跌
        public bool RequireDowntrend { get; set; } = true;

        // 下跌判斷：前一天收盤相對 N 天前收盤的跌幅
        public int DowntrendDays { get; set; } = 5;
        public double DowntrendPct { get; set; } = 3;

        // 是否要求當天成交量放大
        public bool RequireVolume { get; set; } = false;

        // 成交量判斷：當天成交量 ≥ 前 N 天平均量 × 倍數
        public int VolumeAvgDays { get; set; } = 5;
        public double VolumeMultiplier { get; set; } = 1.5;
    }
}
