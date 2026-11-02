using StockDashboard.Models;
using StockDashboard.Models.Patterns;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Interfaces;
using StockDashboard.Services.Patterns;

namespace StockDashboard.Services
{
    /// <summary>
    /// K 線型態掃描：讀出每檔股票的完整日線 → 逐天判斷型態 → 切出小卡範圍並計算均線與之後的報酬。
    /// </summary>
    public class PatternScanService : IPatternScanService
    {
        private readonly IStockPriceRepository _priceRepository;
        private readonly IStockNameRepository _stockNameRepository;

        public PatternScanService(IStockPriceRepository priceRepository, IStockNameRepository stockNameRepository)
        {
            _priceRepository = priceRepository;
            _stockNameRepository = stockNameRepository;
        }

        public async Task<PatternScanResult> ScanAsync(
            IEnumerable<string> stockIds, ICandlePattern pattern, PatternParameterValues values, int daysBefore = 10, int daysAfter = 20)
        {
            var ids = stockIds.Distinct().ToList();
            var result = new PatternScanResult { PatternName = pattern.Name };
            var names = await _stockNameRepository.GetStockNamesOrEmptyAsync(ids);

            foreach (var stockId in ids)
            {
                var candles = (await _priceRepository.GetAllCandlesAsync(stockId)).ToList();
                if (candles.Count == 0)
                {
                    result.StocksWithoutData.Add(stockId);
                    continue;
                }

                var matches = FindMatches(stockId, candles, pattern, values, daysBefore, daysAfter);
                names.TryGetValue(stockId, out var name);
                matches.ForEach(m => m.StockName = name);
                result.Matches.AddRange(matches);
            }

            // 新的日期在前；同一天依代號排序
            result.Matches = result.Matches
                .OrderByDescending(m => m.SignalDate, StringComparer.Ordinal)
                .ThenBy(m => m.StockId, StringComparer.Ordinal)
                .ToList();
            return result;
        }

        /// <summary>
        /// 在單檔股票的完整日線 (由舊到新) 中找出所有符合的日子，並組成小卡資料。
        /// </summary>
        public static List<PatternMatch> FindMatches(
            string stockId, IReadOnlyList<StockDataPoint> candles, ICandlePattern pattern, PatternParameterValues values, int daysBefore, int daysAfter)
        {
            var matches = new List<PatternMatch>();
            if (candles.Count < pattern.BarCount) return matches;

            // 均線以完整歷史計算，小卡最左邊的均線才會是正確值
            double?[] ma5 = MovingAverage(candles, 5);
            double?[] ma20 = MovingAverage(candles, 20);

            for (int i = pattern.BarCount - 1; i < candles.Count; i++)
            {
                if (!pattern.IsMatch(candles, i, values)) continue;

                int start = Math.Max(0, i - daysBefore);
                int end = Math.Min(candles.Count - 1, i + daysAfter);

                var window = new List<PatternCandle>();
                for (int k = start; k <= end; k++)
                {
                    var c = candles[k];
                    window.Add(new PatternCandle
                    {
                        Date = c.Date, Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume,
                        Ma5 = ma5[k], Ma20 = ma20[k]
                    });
                }

                matches.Add(new PatternMatch
                {
                    StockId = stockId,
                    SignalDate = candles[i].Date,
                    SignalIndex = i - start,
                    PatternBarCount = pattern.BarCount,
                    Candles = window,
                    Return5 = ReturnAfter(candles, i, 5),
                    Return10 = ReturnAfter(candles, i, 10),
                    Return20 = ReturnAfter(candles, i, 20)
                });
            }

            return matches;
        }

        // 第 index 天收盤到第 index + days 天收盤的報酬 %，四捨五入到小數兩位；資料不足為 null
        private static double? ReturnAfter(IReadOnlyList<StockDataPoint> candles, int index, int days)
        {
            if (index + days >= candles.Count || candles[index].Close <= 0) return null;
            return Math.Round((candles[index + days].Close - candles[index].Close) / candles[index].Close * 100, 2);
        }

        private static double?[] MovingAverage(IReadOnlyList<StockDataPoint> candles, int period)
        {
            var result = new double?[candles.Count];
            double sum = 0;
            for (int i = 0; i < candles.Count; i++)
            {
                sum += candles[i].Close;
                if (i >= period) sum -= candles[i - period].Close;
                if (i >= period - 1) result[i] = Math.Round(sum / period, 2);
            }
            return result;
        }
    }
}
