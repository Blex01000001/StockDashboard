namespace StockDashboard.Models
{
    /// <summary>
    /// 證交所 STOCK_DAY API 回傳的根結構 (只取用到的欄位)。
    /// Data 每一列依序為：0:日期(民國) 1:成交股數 2:成交金額 3:開盤價 4:最高價 5:最低價 6:收盤價 7:漲跌價差 8:成交筆數
    /// </summary>
    public class TwseStockDayResponse
    {
        public string Stat { get; set; } = string.Empty;
        public List<List<string>> Data { get; set; } = new();
    }
}
