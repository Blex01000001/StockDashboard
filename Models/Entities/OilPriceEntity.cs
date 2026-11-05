namespace StockDashboard.Models.Entities
{
    /// <summary>
    /// 每日原油價格 (對應 SQLite OilPriceDaily 表格)。
    /// 來源 oilpriceapi.com 的 past_year：每天一筆「當日均價」(daily_average)，不是收盤價，單位美元 / 桶。
    /// </summary>
    public class OilPriceEntity
    {
        public string Code { get; set; } = string.Empty;        // 例如 BRENT_CRUDE_USD
        public string TradeDate { get; set; } = string.Empty;   // yyyy-MM-dd (UTC 日期)
        public double Price { get; set; }
    }
}
