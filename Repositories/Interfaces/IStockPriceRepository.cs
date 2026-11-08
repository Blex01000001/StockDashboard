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

        // 取得單檔股票全部 K 線資料 (由舊到新)，略過開高低收有缺值的日子 (用於型態掃描)
        Task<IEnumerable<StockDataPoint>> GetAllCandlesAsync(string stockId);


        // 新增或覆寫日線資料 (INSERT OR REPLACE，主鍵為 StockId + TradeDate)，回傳寫入筆數
        Task<int> UpsertPricesAsync(IEnumerable<StockDailyPriceEntity> prices);
    }
}
