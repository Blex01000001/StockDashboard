namespace StockDashboard.Models.Entities
{
    /// <summary>
    /// 每日指數 K 線 (對應 SQLite MarketIndexDaily 表格)，例如美國費城半導體指數 SOX。
    /// TradeDate 是該指數所在市場的交易日 (美股的美東日期)，不是台灣日期。
    /// </summary>
    public class MarketIndexEntity
    {
        public string Code { get; set; } = string.Empty;        // 例如 SOX
        public string TradeDate { get; set; } = string.Empty;   // yyyy-MM-dd
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public long Volume { get; set; }
    }
}
