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
        // 預設的觀察天數 (報酬與勝率)
        public static readonly int[] DefaultHorizons = { 5, 10, 20 };

        private readonly IStockPriceRepository _priceRepository;
        private readonly IStockNameRepository _stockNameRepository;

        public PatternScanService(IStockPriceRepository priceRepository, IStockNameRepository stockNameRepository)
        {
            _priceRepository = priceRepository;
            _stockNameRepository = stockNameRepository;
        }

        public async Task<PatternScanResult> ScanAsync(
            IEnumerable<string> stockIds, ICandlePattern pattern, PatternParameterValues values,
            int daysBefore = 10, int daysAfter = 20, IEnumerable<int>? horizons = null)
        {
            var ids = stockIds.Distinct().ToList();
            var days = (horizons ?? DefaultHorizons).Where(d => d > 0).Distinct().OrderBy(d => d).ToList();
            var result = new PatternScanResult
            {
                PatternName = pattern.Name,
                IsBullish = pattern.Direction == PatternDirection.Bullish,
                Horizons = days
            };
            var names = await _stockNameRepository.GetStockNamesOrEmptyAsync(ids);

            foreach (var stockId in ids)
            {
                var candles = (await _priceRepository.GetAllCandlesAsync(stockId)).ToList();
                if (candles.Count == 0)
                {
                    result.StocksWithoutData.Add(stockId);
                    continue;
                }

                var matches = FindMatches(stockId, candles, pattern, values, daysBefore, daysAfter, days);
                names.TryGetValue(stockId, out var name);
                matches.ForEach(m => m.StockName = name);
                result.Matches.AddRange(matches);
            }

            // 新的日期在前；同一天依代號排序
            result.Matches = result.Matches
                .OrderByDescending(m => m.SignalDate, StringComparer.Ordinal)
                .ThenBy(m => m.StockId, StringComparer.Ordinal)
                .ToList();
            result.WinRates = ComputeWinRates(result.Matches, pattern.Direction, days);
            return result;
        }

        /// <summary>
        /// 在單檔股票的完整日線 (由舊到新) 中找出所有符合的日子，並組成小卡資料。
        /// </summary>
        public static List<PatternMatch> FindMatches(
            string stockId, IReadOnlyList<StockDataPoint> candles, ICandlePattern pattern, PatternParameterValues values,
            int daysBefore, int daysAfter, IReadOnlyList<int>? horizons = null)
        {
            horizons ??= DefaultHorizons;
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
                    // 報酬用完整歷史計算，所以觀察天數可以超過小卡顯示的「後 20 天」
                    Returns = horizons.ToDictionary(d => d, d => ReturnAfter(candles, i, d))
                });
            }

            return matches;
        }

        /// <summary>
        /// 各觀察天數的勝率：看多型態之後報酬 &gt; 0 算勝，看空型態之後報酬 &lt; 0 算勝；報酬剛好 0 算輸。
        /// </summary>
        public static List<PatternWinRate> ComputeWinRates(IReadOnlyList<PatternMatch> matches, PatternDirection direction, IEnumerable<int>? horizons = null)
        {
            int sign = direction == PatternDirection.Bullish ? 1 : -1;
            return (horizons ?? DefaultHorizons).Select(days =>
            {
                var returns = matches.Select(m => m.ReturnAfter(days)).Where(r => r.HasValue).Select(r => r!.Value).ToList();
                return new PatternWinRate { Days = days, Total = returns.Count, Wins = returns.Count(r => sign * r > 0) };
            }).ToList();
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
