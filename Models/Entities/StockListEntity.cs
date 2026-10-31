namespace StockDashboard.Models.Entities
{
    /// <summary>
    /// 股票清單實體 (對應 SQLite StockList 表格)
    /// </summary>
    public class StockListEntity
    {
        /// <summary>
        /// 股票代號 (主鍵)，例如 "2330"、"0050"、"00981A"
        /// </summary>
        public string StockId { get; set; } = string.Empty;

        /// <summary>
        /// 股票簡稱，例如 "台積電"
        /// </summary>
        public string StockName { get; set; } = string.Empty;

        /// <summary>
        /// 市場別："TWSE" = 上市；預留 "TPEx" = 上櫃
        /// </summary>
        public string Market { get; set; } = "TWSE";

        /// <summary>
        /// 本機最後更新時間 (yyyy-MM-dd HH:mm:ss)
        /// </summary>
        public string UpdatedAt { get; set; } = string.Empty;
    }
}
