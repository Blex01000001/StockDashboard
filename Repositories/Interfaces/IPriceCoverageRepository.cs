namespace StockDashboard.Repositories.Interfaces
{
    /// <summary>
    /// 日線「已抓到哪裡」的記錄，供依產業批次同步續跑用 (已完整的月份不重抓)。
    /// 由 StockPriceRepository 實作 (StockDailyPrice 的唯一讀寫入口)。
    /// </summary>
    public interface IPriceCoverageRepository
    {
        // 某檔股票每個月份 ("yyyy-MM") 在資料庫裡的最後一個交易日 (yyyy-MM-dd)；沒資料的月份不會出現
        Task<Dictionary<string, string>> GetMonthLastDatesAsync(string stockId);

        // 是否已經追溯到該股票的上市日 (往前連續多個月沒有資料)
        Task<bool> IsHistoryReachedAsync(string stockId);

        Task SetHistoryReachedAsync(string stockId);
    }
}
