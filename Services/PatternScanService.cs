using StockDashboard.Models;
using StockDashboard.Models.Entities;
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
        private readonly IInstitutionalRepository _institutionalRepository;
        private readonly IOilPriceRepository _oilPriceRepository;
        private readonly IMarketIndexRepository _marketIndexRepository;
        private readonly IStockCorporateActionRepository _actionRepository;

        public PatternScanService(IStockPriceRepository priceRepository, IStockNameRepository stockNameRepository,
            IInstitutionalRepository institutionalRepository, IOilPriceRepository oilPriceRepository, IMarketIndexRepository marketIndexRepository,
            IStockCorporateActionRepository actionRepository)
        {
            _actionRepository = actionRepository;
            _marketIndexRepository = marketIndexRepository;
            _priceRepository = priceRepository;
            _stockNameRepository = stockNameRepository;
            _institutionalRepository = institutionalRepository;
            _oilPriceRepository = oilPriceRepository;
        }

        public async Task<PatternScanResult> ScanAsync(
            IEnumerable<string> stockIds, ICandlePattern pattern, PatternParameterValues values,
            int daysBefore = 10, int daysAfter = 20, IEnumerable<int>? horizons = null, IndicatorSettings? indicators = null, bool adjusted = false)
        {
            var ids = stockIds.Distinct().ToList();
            var days = (horizons ?? DefaultHorizons).Where(d => d > 0).Distinct().OrderBy(d => d).ToList();
            var result = new PatternScanResult
            {
                PatternName = pattern.Name,
                IsBullish = pattern.Direction == PatternDirection.Bullish,
                Adjusted = adjusted,
                Horizons = days,
                DaysBefore = daysBefore,
                DaysAfter = daysAfter
            };
            var names = await _stockNameRepository.GetStockNamesOrEmptyAsync(ids);
            var oil = await _oilPriceRepository.GetAllOrEmptyAsync(OilPriceApiClient.BrentCode);   // 與個股無關，整次掃描只讀一次
            var sox = await _marketIndexRepository.GetAllOrEmptyAsync(MarketIndexApiClient.SoxCode);

            foreach (var stockId in ids)
            {
                var candles = (await _priceRepository.GetAllCandlesAsync(stockId)).ToList();
                // 還原股價：型態判斷、之後報酬、指標都用還原後的價格；沒有除權息事件的股票不變
                if (adjusted && candles.Count > 0) candles = PriceAdjuster.ApplyToCandles(candles, await _actionRepository.GetByStockIdAsync(stockId));
                if (candles.Count == 0)
                {
                    result.StocksWithoutData.Add(stockId);
                    continue;
                }

                var matches = FindMatches(stockId, candles, pattern, values, daysBefore, daysAfter, days, indicators);
                if (matches.Count > 0) result.FullCandles[stockId] = candles;
                names.TryGetValue(stockId, out var name);
                matches.ForEach(m => m.StockName = name);
                if (matches.Count > 0)
                {
                    ApplyInstitutional(matches, await _institutionalRepository.GetByStockOrEmptyAsync(stockId));
                    ApplyOil(matches.SelectMany(m => m.Candles), oil);
                    ApplyMarketIndex(matches.SelectMany(m => m.Candles), candles.Select(c => c.Date).ToList(), sox);
                }
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
        /// 依日期把單檔股票的三大法人資料填入小卡 K 棒；沒有對應日期的維持 null (圖上留空)。
        /// </summary>
        public static void ApplyInstitutional(IEnumerable<PatternMatch> matchesOfStock, IEnumerable<InstitutionalDailyEntity> rows) =>
            ApplyInstitutional(matchesOfStock.SelectMany(m => m.Candles), rows);

        public static void ApplyInstitutional(IEnumerable<PatternCandle> candles, IEnumerable<InstitutionalDailyEntity> rows)
        {
            var byDate = new Dictionary<string, InstitutionalDailyEntity>();
            foreach (var row in rows) byDate[row.TradeDate] = row;

            foreach (var candle in candles)
            {
                if (!byDate.TryGetValue(candle.Date, out var row)) continue;
                candle.TotalNet = row.TotalNet;
                candle.ForeignNet = row.ForeignTotalNet;
                candle.TrustNet = row.TrustNet;
                candle.DealerNet = row.DealerNet;
                candle.ForeignHoldingPct = row.ForeignHoldingPct;
            }
        }

        /// <summary>
        /// 依日期填入 Brent 原油價格 (oil 由舊到新)：每根 K 棒取「當天之前」最近一筆 (嚴格早於當天)，休市日自然沿用前值。
        /// 原因：來源是 UTC 整天的均價，涵蓋台股收盤之後，用當天的值等於偷看未來。找不到更早的資料為 null (圖上留空)。
        /// </summary>
        public static void ApplyOil(IEnumerable<PatternCandle> candles, IReadOnlyList<OilPriceEntity> oil)
        {
            if (oil.Count == 0) return;
            var dates = oil.Select(o => o.TradeDate).ToArray();
            foreach (var candle in candles)
            {
                int i = Array.BinarySearch(dates, candle.Date, StringComparer.Ordinal);
                int previous = (i >= 0 ? i : ~i) - 1;   // 找到同一天也要往前一筆
                candle.Oil = previous >= 0 ? oil[previous].Price : null;
            }
        }

        /// <summary>
        /// 依日期填入美股指數 (費半) K 線，對齊規則：美股交易日 D 歸屬到「該股完整日線中第一個日期 &gt; D」的台股交易日 T
        /// (美股在台灣時間隔天清晨收盤，所以 T 開盤前看到的是 T 之前已收盤的美股)。
        /// 同一個 T 有多個美股交易日 (台股連假) 時合併成一根：開盤取最早一天、收盤取最後一天、最高取最大、最低取最小，
        /// 這樣不會漏掉連假期間的美股行情；T 之前沒有新的美股交易日 (美股休市) 時該日為 null，不重複顯示同一根。
        /// tradingDates = 該股完整日線的日期 (由舊到新)；index 由舊到新；晚於最後一個台股日的美股資料忽略。
        /// 費半均線 (SoxMa5 ~ SoxMa120) 用 index 的美股交易日序列算 (MA5 = 最近 5 個美股交易日)，不是對齊後的台股日；
        /// 合併成一根時取最後一個美股交易日的均線值。
        /// </summary>
        public static void ApplyMarketIndex(IEnumerable<PatternCandle> candles, IReadOnlyList<string> tradingDates, IReadOnlyList<MarketIndexEntity> index)
        {
            if (index.Count == 0 || tradingDates.Count == 0) return;

            // 均線用完整的美股序列算 (含這次沒對應到任何台股日的資料)，之後再依美股日期對應到台股日
            var closes = index.Select(r => r.Close).ToList();
            var ma5 = TechnicalIndicators.MovingAverage(closes, 5);
            var ma10 = TechnicalIndicators.MovingAverage(closes, 10);
            var ma20 = TechnicalIndicators.MovingAverage(closes, 20);
            var ma60 = TechnicalIndicators.MovingAverage(closes, 60);
            var ma120 = TechnicalIndicators.MovingAverage(closes, 120);

            var byTaiwanDate = new Dictionary<string, MarketIndexEntity>();
            var lastSession = new Dictionary<string, int>();   // 台股日 → 合併後最後一個美股交易日在 index 的位置
            var dates = tradingDates as string[] ?? tradingDates.ToArray();
            for (int r = 0; r < index.Count; r++)
            {
                var row = index[r];
                int i = Array.BinarySearch(dates, row.TradeDate, StringComparer.Ordinal);
                int position = i >= 0 ? i + 1 : ~i;   // 第一個「大於」美股日期的台股日 (同一天也要往後一個)
                if (position >= dates.Length) continue;

                string taiwanDate = dates[position];
                lastSession[taiwanDate] = r;
                if (byTaiwanDate.TryGetValue(taiwanDate, out var merged))
                {
                    merged.High = Math.Max(merged.High, row.High);
                    merged.Low = Math.Min(merged.Low, row.Low);
                    merged.Close = row.Close;
                    merged.TradeDate = row.TradeDate;   // 記錄合併後最後一個美股交易日
                }
                else
                {
                    byTaiwanDate[taiwanDate] = new MarketIndexEntity { Code = row.Code, TradeDate = row.TradeDate, Open = row.Open, High = row.High, Low = row.Low, Close = row.Close };
                }
            }

            foreach (var candle in candles)
            {
                byTaiwanDate.TryGetValue(candle.Date, out var m);
                candle.SoxOpen = m?.Open;
                candle.SoxHigh = m?.High;
                candle.SoxLow = m?.Low;
                candle.SoxClose = m?.Close;
                candle.SoxDate = m?.TradeDate;
                if (m == null)
                {
                    candle.SoxMa5 = candle.SoxMa10 = candle.SoxMa20 = candle.SoxMa60 = candle.SoxMa120 = null;
                    continue;
                }
                int s = lastSession[candle.Date];
                candle.SoxMa5 = ma5[s]; candle.SoxMa10 = ma10[s]; candle.SoxMa20 = ma20[s]; candle.SoxMa60 = ma60[s]; candle.SoxMa120 = ma120[s];
            }
        }

        /// <summary>
        /// 在單檔股票的完整日線 (由舊到新) 中找出所有符合的日子，並組成小卡資料。
        /// </summary>
        public static List<PatternMatch> FindMatches(
            string stockId, IReadOnlyList<StockDataPoint> candles, ICandlePattern pattern, PatternParameterValues values,
            int daysBefore, int daysAfter, IReadOnlyList<int>? horizons = null, IndicatorSettings? indicators = null)
        {
            horizons ??= DefaultHorizons;
            var matches = new List<PatternMatch>();
            if (candles.Count < pattern.BarCount) return matches;

            for (int i = pattern.BarCount - 1; i < candles.Count; i++)
            {
                if (!pattern.IsMatch(candles, i, values)) continue;

                int start = Math.Max(0, i - daysBefore);
                int end = Math.Min(candles.Count - 1, i + daysAfter);

                var window = new List<PatternCandle>();
                for (int k = start; k <= end; k++)
                {
                    var c = candles[k];
                    window.Add(PatternCandle.From(c));
                }

                matches.Add(new PatternMatch
                {
                    StockId = stockId,
                    SignalDate = candles[i].Date,
                    SignalIndex = i - start,
                    PatternBarCount = pattern.BarCount,
                    WindowStart = start,
                    Candles = window,
                    // 報酬用完整歷史計算，所以觀察天數可以超過小卡顯示的「後 20 天」
                    Returns = horizons.ToDictionary(d => d, d => ReturnAfter(candles, i, d))
                });
            }

            ApplyIndicators(matches, candles, indicators ?? new IndicatorSettings());
            return matches;
        }

        /// <summary>
        /// 以完整日線計算技術指標 (均線、量均線、MACD、KD、RSI)，填入同一檔股票各小卡的 K 棒。
        /// 用完整歷史計算，小卡最左邊的數值才會正確。
        /// </summary>
        public static void ApplyIndicators(IEnumerable<PatternMatch> matchesOfStock, IReadOnlyList<StockDataPoint> candles, IndicatorSettings s) =>
            ApplyIndicators(matchesOfStock.Select(m => (m.WindowStart, (IReadOnlyList<PatternCandle>)m.Candles)), candles, s);

        /// <summary>
        /// 同上，對任意顯示範圍：Start = 範圍第一根在完整日線中的位置 (技術圖表頁也用這個)。
        /// </summary>
        public static void ApplyIndicators(IEnumerable<(int Start, IReadOnlyList<PatternCandle> Candles)> windows, IReadOnlyList<StockDataPoint> candles, IndicatorSettings s)
        {
            var closes = candles.Select(c => c.Close).ToList();
            var volumes = candles.Select(c => (double)c.Volume).ToList();
            double?[] ma5 = TechnicalIndicators.MovingAverage(closes, 5);
            double?[] ma10 = TechnicalIndicators.MovingAverage(closes, 10);
            double?[] ma20 = TechnicalIndicators.MovingAverage(closes, 20);
            double?[] ma60 = TechnicalIndicators.MovingAverage(closes, 60);
            double?[] ma120 = TechnicalIndicators.MovingAverage(closes, 120);
            double?[] volMa5 = TechnicalIndicators.MovingAverage(volumes, 5, decimals: 0);
            double?[] volMa10 = TechnicalIndicators.MovingAverage(volumes, 10, decimals: 0);
            var (dif, macd, osc) = TechnicalIndicators.Macd(closes, s.MacdFast, s.MacdSlow, s.MacdSignal);
            var (kLine, dLine) = TechnicalIndicators.Kd(
                candles.Select(c => c.High).ToList(), candles.Select(c => c.Low).ToList(), closes, s.KdPeriod, s.KdKSmooth, s.KdDSmooth);
            double?[] rsiFast = TechnicalIndicators.Rsi(closes, s.RsiFast);
            double?[] rsiSlow = TechnicalIndicators.Rsi(closes, s.RsiSlow);
            var (bollUpper, bollLower) = s.Bollinger
                ? TechnicalIndicators.Bollinger(closes, s.BollPeriod, s.BollMultiplier)
                : (new double?[closes.Count], new double?[closes.Count]);

            foreach (var (start, window) in windows)
            {
                for (int j = 0; j < window.Count; j++)
                {
                    int k = start + j;
                    var c = window[j];
                    c.Ma5 = ma5[k]; c.Ma10 = ma10[k]; c.Ma20 = ma20[k]; c.Ma60 = ma60[k]; c.Ma120 = ma120[k];
                    c.VolMa5 = volMa5[k]; c.VolMa10 = volMa10[k];
                    c.Dif = dif[k]; c.Macd = macd[k]; c.Osc = osc[k];
                    c.K = kLine[k]; c.D = dLine[k];
                    c.RsiFast = rsiFast[k]; c.RsiSlow = rsiSlow[k];
                    c.BollUpper = bollUpper[k]; c.BollLower = bollLower[k];
                }
            }
        }

        /// <summary>
        /// 調整指標參數後重算整份結果 (使用掃描時保留的完整日線，不查資料庫)。
        /// </summary>
        public static void RecomputeIndicators(PatternScanResult result, IndicatorSettings settings)
        {
            foreach (var group in result.Matches.GroupBy(m => m.StockId))
            {
                if (result.FullCandles.TryGetValue(group.Key, out var candles))
                    ApplyIndicators(group, candles, settings);
            }
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
