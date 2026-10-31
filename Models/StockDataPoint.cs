namespace StockDashboard.Models
{
    /// <summary>
    /// K 線圖用的單日資料點。
    /// 屬性名稱對應 SQL 的 AS 別名；傳到 JS 時會自動轉成 camelCase (date / open / high / low / close / volume)。
    /// </summary>
    public class StockDataPoint
    {
        public string Date { get; set; } = string.Empty;
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public long Volume { get; set; }
    }
}
