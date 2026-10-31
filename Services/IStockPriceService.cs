using StockDashboard.Models.Entities;

namespace StockDashboard.Services
{
    public interface IStockPriceService
    {
        // 提供選擇性參數：isAdjusted (是否還原)
        Task<IEnumerable<StockDailyPriceEntity>> GetPricesAsync(
            string stockId,
            string startDate,
            string endDate,
            bool isAdjusted = false); // 預設為 False (原始股價)
    }
}
