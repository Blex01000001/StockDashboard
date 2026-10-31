namespace StockDashboard.Models
{
    public class StockDailyPrice
    {
        public string StockId { get; set; } = string.Empty;
        public string TradeDate { get; set; } = string.Empty; // 建議儲存格式：YYYY-MM-DD
        public long? Volume { get; set; }                     // 成交股數
        public long? Amount { get; set; }                     // 成交金額
        public double? OpenPrice { get; set; }
        public double? HighPrice { get; set; }
        public double? LowPrice { get; set; }
        public double? ClosePrice { get; set; }
        public double? PriceChange { get; set; }              // 漲跌價差
        public long? TradeCount { get; set; }                 // 成交筆數
        public string? Note { get; set; }
    }
}
