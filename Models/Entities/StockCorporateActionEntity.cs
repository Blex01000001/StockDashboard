namespace StockDashboard.Models.Entities
{
    /// <summary>
    /// 除權息事件 (對應 SQLite StockExRightEvent 表)。
    /// 只放與資料來源無關的欄位：還原係數 = RefPrice / PreClose。
    /// 現金股利、股票股利等細節已含在參考價裡，不另存。
    /// </summary>
    public class StockCorporateActionEntity
    {
        /// <summary>股票代號</summary>
        public string StockId { get; set; } = string.Empty;

        /// <summary>除權息交易日 (yyyy-MM-dd)；這一天起的價格已是除權息後的價格</summary>
        public string ExDate { get; set; } = string.Empty;

        /// <summary>除權息前一交易日收盤價</summary>
        public double PreClose { get; set; }

        /// <summary>除權息參考價 (開盤競價基準)</summary>
        public double RefPrice { get; set; }
    }
}
