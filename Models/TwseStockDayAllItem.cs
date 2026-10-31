namespace StockDashboard.Models
{
    /// <summary>
    /// 證交所 OpenAPI「上市個股日成交資訊」(STOCK_DAY_ALL) 的單筆資料，只取用代號與名稱。
    /// 回傳內容涵蓋當日有交易的上市股票、ETF、ETN 等。
    /// </summary>
    public class TwseStockDayAllItem
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
