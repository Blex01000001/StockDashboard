using StockDashboard.Models;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Services
{
    public class StockPerformanceService : IStockPerformanceService
    {
        private readonly IStockPriceRepository _priceRepository;

        // 建構子注入 Repository
        public StockPerformanceService(IStockPriceRepository priceRepository)
        {
            _priceRepository = priceRepository;
        }

        public async Task<CompareChartResult> GetCompareChartDataAsync(List<string> stockIds, int days, int rangeDay)
        {
            var result = new CompareChartResult();

            // 設定撈取區間：安全起見撈取過去一年 (365天) 的資料作為基準
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

            // 2. 核心運算：計算滾動報酬率 (與原本邏輯完全一致，只是變數改為強型別 Entity)
            foreach (var group in grouped)
            {
                string currentStockId = group.Key;
                var priceDict = group.ToDictionary(r => r.TradeDate, r => r.ClosePrice);
                var returnRatesSeries = new List<object>();

                for (int i = 0; i < allTimelineDates.Count; i++)
                {
                    string currentDate = allTimelineDates[i];

                    if (!priceDict.TryGetValue(currentDate, out double? currentPrice)) // 引數 2: 無法從 'out double' 轉換成 'out double?'
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

                    if (priceDict.TryGetValue(compareDate, out double? comparePrice) && comparePrice > 0) // 引數 2: 無法從 'out double' 轉換成 'out double?'
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
                    Name = currentStockId,
                    Data = returnRatesSeries
                });
            }

            return result;
        }
    }
}
