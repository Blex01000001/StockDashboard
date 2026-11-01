using StockDashboard.Models;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Services
{
    public class StockPerformanceService : IStockPerformanceService
    {
        private readonly IStockPriceRepository _priceRepository;
        private readonly IStockListRepository _stockListRepository;

        // 建構子注入 Repository
        public StockPerformanceService(IStockPriceRepository priceRepository, IStockListRepository stockListRepository)
        {
            _priceRepository = priceRepository;
            _stockListRepository = stockListRepository;
        }

        /// <param name="stockIds">要比較的股票代號</param>
        /// <param name="days">報酬區間：往前比較幾個「交易日」(以所有股票聯集後的時間軸計算)</param>
        /// <param name="rangeDay">從資料庫撈取的日曆天數範圍</param>
        public async Task<CompareChartResult> GetCompareChartDataAsync(List<string> stockIds, int days, int rangeDay = 660)
        {
            var result = new CompareChartResult();

            // 設定撈取區間：從今天往回 rangeDay 個日曆天
            string startDate = DateTime.Today.AddDays(-rangeDay).ToString("yyyy-MM-dd");
            string endDate = DateTime.Today.ToString("yyyy-MM-dd");

            // 呼叫 Repository 獲取資料
            var rawRows = await _priceRepository.GetMultipleHistoricalPricesAsync(stockIds, startDate, endDate);
            var rawRowsList = rawRows.ToList();

            if (!rawRowsList.Any()) return result;

            // 1. 取得所有股票「聯集」後的完整日期時間軸
            var allTimelineDates = rawRowsList.Select(r => r.TradeDate).Distinct().OrderBy(d => d).ToList();
            result.Timeline = allTimelineDates;

            var grouped = rawRowsList.GroupBy(r => r.StockId);

            // 圖例顯示「代號 名稱」(例如 "0050 元大台灣50")；股票清單中沒有的只顯示代號
            var stockNames = await _stockListRepository.GetStockNamesOrEmptyAsync(stockIds);

            // 2. 核心運算：計算滾動報酬率 = (今日收盤 - N 個交易日前收盤) / N 個交易日前收盤 × 100
            foreach (var group in grouped)
            {
                string currentStockId = group.Key;
                var priceDict = group.ToDictionary(r => r.TradeDate, r => r.ClosePrice);
                var returnRatesSeries = new List<double?>();

                for (int i = 0; i < allTimelineDates.Count; i++)
                {
                    string currentDate = allTimelineDates[i];

                    if (!priceDict.TryGetValue(currentDate, out double? currentPrice) || currentPrice == null)
                    {
                        returnRatesSeries.Add(null);
                        continue;
                    }

                    int targetIndex = i - days;

                    if (targetIndex < 0)
                    {
                        returnRatesSeries.Add(null);
                        continue;
                    }

                    string compareDate = allTimelineDates[targetIndex];

                    if (priceDict.TryGetValue(compareDate, out double? comparePrice) && comparePrice > 0)
                    {
                        double rate = ((currentPrice.Value - comparePrice.Value) / comparePrice.Value) * 100;
                        returnRatesSeries.Add(Math.Round(rate, 2));
                    }
                    else
                    {
                        returnRatesSeries.Add(null);
                    }
                }

                result.SeriesData.Add(new StockSeries
                {
                    Name = stockNames.TryGetValue(currentStockId, out var stockName)
                        ? $"{currentStockId} {stockName}"
                        : currentStockId,
                    Data = returnRatesSeries
                });
            }

            return result;
        }
    }
}
