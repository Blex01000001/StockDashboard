using StockDashboard.Repositories.Interfaces;
using StockDashboard.Models.Entities;

namespace StockDashboard.Services
{
    public class StockPriceService : IStockPriceService
    {
        private readonly IStockPriceRepository _priceRepo;
        private readonly IStockDividendRepository _dividendRepo;

        public StockPriceService(IStockPriceRepository priceRepo, IStockDividendRepository dividendRepo)
        {
            _priceRepo = priceRepo;
            _dividendRepo = dividendRepo;
        }

        public async Task<IEnumerable<StockDailyPriceEntity>> GetPricesAsync(
            string stockId, string startDate, string endDate, bool isAdjusted)
        {
            // 1. 取得原始資料
            var prices = await _priceRepo.GetHistoricalPricesAsync(stockId, startDate, endDate);

            if (!isAdjusted) return prices;

            // 2. 若要求還原，則取得除權息資料進行計算
            var dividends = await _dividendRepo.GetDividendsByStockIdAsync(stockId); // 假設有此方法

            // 3. 執行還原算法：將股價根據除權息係數進行回溯調整
            return ApplyDividendAdjustment(prices, dividends);
        }

        private IEnumerable<StockDailyPriceEntity> ApplyDividendAdjustment(
            IEnumerable<StockDailyPriceEntity> prices, 
            IEnumerable<StockDividendEntity> dividends)
        {
            // 實作還原演算法 (例如：(除權息前股價 - 現金股利) / 除權係數...)
            throw new NotImplementedException();
        }
    }
}
