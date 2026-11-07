using StockDashboard.Repositories.Interfaces;
using StockDashboard.Models.Entities;

namespace StockDashboard.Services
{
    public class StockPriceService : IStockPriceService
    {
        private readonly IStockPriceRepository _priceRepo;
        private readonly IStockCorporateActionRepository _actionRepo;

        public StockPriceService(IStockPriceRepository priceRepo, IStockCorporateActionRepository actionRepo)
        {
            _priceRepo = priceRepo;
            _actionRepo = actionRepo;
        }

        public async Task<IEnumerable<StockDailyPriceEntity>> GetPricesAsync(
            string stockId, string startDate, string endDate, bool isAdjusted)
        {
            var prices = await _priceRepo.GetHistoricalPricesAsync(stockId, startDate, endDate);
            if (!isAdjusted) return prices;

            // 還原：除權息日之前的價格乘上係數；事件要取完整歷史 (包含查詢區間之後的事件)
            var actions = await _actionRepo.GetByStockIdAsync(stockId);
            return PriceAdjuster.Apply(prices, actions);
        }
    }
}
