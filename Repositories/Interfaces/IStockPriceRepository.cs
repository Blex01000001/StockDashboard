using StockDashboard.Models;
using StockDashboard.Models.Entities;

namespace StockDashboard.Repositories.Interfaces
{
    public interface IStockPriceRepository
    {
        // 取得單檔股票在特定區間的日線資料
        Task<IEnumerable<StockDailyPriceEntity>> GetHistoricalPricesAsync(string stockId, string startDate, string endDate);

        // 取得多檔股票特定區間的日線資料 (用於多檔比較服務)
        Task<IEnumerable<StockDailyPriceEntity>> GetMultipleHistoricalPricesAsync(IEnumerable<string> stockIds, string startDate, string endDate);

        // 取得單檔股票「最新」N 筆 K 線資料 (依日期由舊到新排序，用於 K 線圖)
        Task<IEnumerable<StockDataPoint>> GetLatestCandlesAsync(string stockId, int count);

        // 取得單檔股票最新一筆日線資料；查無資料回傳 null (用於股價查詢頁)
        Task<StockDailyPriceEntity?> GetLatestPriceAsync(string stockId);

        // 新增或覆寫日線資料 (INSERT OR REPLACE，主鍵為 StockId + TradeDate)，回傳寫入筆數
        Task<int> UpsertPricesAsync(IEnumerable<StockDailyPriceEntity> prices);
    }
}
