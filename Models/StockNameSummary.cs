namespace StockDashboard.Models
{
    /// <summary>
    /// 股票名稱來源的統計 (資料更新頁顯示用)
    /// </summary>
    public class StockNameSummary
    {
        // StockList (上市公司基本資料) 的公司數；資料表不存在或找不到「公司代號 / 公司簡稱」欄位時為 null
        public int? CompanyCount { get; set; }

        // StockNameList (證交所同步清單，含 ETF) 的筆數
        public int SyncedCount { get; set; }

        // StockNameList 最後同步時間；尚未同步過為 null
        public string? LastSyncedAt { get; set; }
    }
}
