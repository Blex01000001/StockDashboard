using StockDashboard.Models.Entities;

namespace StockDashboard.Repositories.Interfaces
{
    public interface IStockPriceRepository
    {
        // 取得單檔股票在特定區間的日線資料
        Task<IEnumerable<StockDailyPriceEntity>> GetHistoricalPricesAsync(string stockId, string startDate, string endDate);

        // 取得多檔股票特定區間的日線資料 (用於多檔比較服務)
        Task<IEnumerable<StockDailyPriceEntity>> GetMultipleHistoricalPricesAsync(IEnumerable<string> stockIds, string startDate, string endDate);
    }
}
