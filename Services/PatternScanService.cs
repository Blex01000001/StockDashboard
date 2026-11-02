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

        // 額外觀察天數的上限 (約一年的交易日)
        public const int MaxHorizon = 250;

        // 小卡至少顯示型態成立後幾天
        public const int MinDaysAfter = 20;

        /// <summary>
        /// 小卡要顯示型態成立後幾天：至少 20 天；有更長的觀察天數時延長到最長的那一個，
        /// 讓使用者在小卡上看得到 60 日、120 日後的走勢。
        /// </summary>
        public static int CardDaysAfter(IEnumerable<int> horizons) =>
            Math.Max(MinDaysAfter, horizons.DefaultIfEmpty(0).Max());

        /// <summary>
        /// 解析使用者輸入的額外觀察天數，例如 "60, 120"。
        /// 可用逗號、頓號、空白分隔；非數字與超出 1 ~ MaxHorizon 的值略過；去除重複並由小到大排序。
        /// </summary>
        public static List<int> ParseHorizons(string? input) =>
            (input ?? string.Empty)
                .Split(new[] { ',', '，', '、', ' ', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => int.TryParse(t.Trim(), out int d) ? d : 0)
                .Where(d => d >= 1 && d <= MaxHorizon)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

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
                Horizons = days,
                DaysBefore = daysBefore,
                DaysAfter = daysAfter
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
            var closes = candles.Select(c => c.Close).ToList();
            var volumes = candles.Select(c => (double)c.Volume).ToList();
            double?[] ma5 = TechnicalIndicators.MovingAverage(closes, 5);
            double?[] ma10 = TechnicalIndicators.MovingAverage(closes, 10);
            double?[] ma20 = TechnicalIndicators.MovingAverage(closes, 20);
            double?[] ma60 = TechnicalIndicators.MovingAverage(closes, 60);
            double?[] ma120 = TechnicalIndicators.MovingAverage(closes, 120);
            double?[] volMa5 = TechnicalIndicators.MovingAverage(volumes, 5, decimals: 0);
            double?[] volMa10 = TechnicalIndicators.MovingAverage(volumes, 10, decimals: 0);
            var (dif, macd, osc) = TechnicalIndicators.Macd(closes);

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
                        Ma5 = ma5[k], Ma10 = ma10[k], Ma20 = ma20[k], Ma60 = ma60[k], Ma120 = ma120[k],
                        VolMa5 = volMa5[k], VolMa10 = volMa10[k],
                        Dif = dif[k], Macd = macd[k], Osc = osc[k]
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
    }
}
