namespace StockDashboard.Models.Entities
{
    /// <summary>
    /// 歷史日線股價實體 (對應 SQLite StockDailyPrice 表格)
    /// </summary>
    public class StockDailyPriceEntity
    {
        // --- Primary Keys (NOT NULL) ---

        /// <summary>
        /// 股票代號
        /// </summary>
        public string StockId { get; set; } = string.Empty;

        /// <summary>
        /// 交易日期 (格式建議為 yyyy-MM-dd 或 yyyyMMdd)
        /// </summary>
        public string TradeDate { get; set; } = string.Empty;


        // --- Nullable Columns (未標記 NOT NULL) ---

        /// <summary>
        /// 成交股數 (Volume)
        /// ⚠️ 使用 long：台股熱門股 (如 00940、長榮航) 單日成交股數常突破數十億，超過 int 的上限 (2,147,483,647)，必須用 long。
        /// </summary>
        public long? Volume { get; set; }

        /// <summary>
        /// 成交金額 (Amount)
        /// ⚠️ 使用 long：台積電等權值股單日成交金額動輒數百億台幣，絕對會發生 int 溢位，必須用 long。
        /// </summary>
        public long? Amount { get; set; }

        /// <summary>
        /// 開盤價
        /// </summary>
        public double? OpenPrice { get; set; }

        /// <summary>
        /// 最高價
        /// </summary>
        public double? HighPrice { get; set; }

        /// <summary>
        /// 最低價
        /// </summary>
        public double? LowPrice { get; set; }

        /// <summary>
        /// 收盤價
        /// </summary>
        public double? ClosePrice { get; set; }

        /// <summary>
        /// 漲跌價差
        /// </summary>
        public double? PriceChange { get; set; }

        /// <summary>
        /// 成交筆數
        /// 說明：單檔股票單日成交筆數通常在數十萬筆以內，使用 int (上限 21.4 億) 足以應付。
        /// </summary>
        public int? TradeCount { get; set; }

        /// <summary>
        /// 備註 (例如：除權息、處置股標記等)
        /// </summary>
        public string? Note { get; set; }
    }
}
