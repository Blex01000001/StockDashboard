using StockDashboard.Models;
using StockDashboard.Models.Patterns;

namespace StockDashboard.Services.Patterns
{
    /// <summary>
    /// 多種型態共用的條件 (先下跌、成交量放大) 與產生範例 K 線的輔助方法。
    /// </summary>
    public static class PatternFilters
    {
        public const string RequireDowntrend = "RequireDowntrend";
        public const string DowntrendDays = "DowntrendDays";
        public const string DowntrendPct = "DowntrendPct";
        public const string RequireVolume = "RequireVolume";
        public const string VolumeAvgDays = "VolumeAvgDays";
        public const string VolumeMultiplier = "VolumeMultiplier";

        public static IEnumerable<PatternParameter> DowntrendParameters(bool enabledByDefault = true) => new[]
        {
            new PatternParameter { Key = RequireDowntrend, Label = "要求先下跌", Kind = PatternParameterKind.Toggle, Default = enabledByDefault ? 1 : 0 },
            new PatternParameter { Key = DowntrendDays, Label = "下跌判斷天數", Default = 5, Step = 1, Min = 1, Max = 60, IsInteger = true, EnabledBy = RequireDowntrend },
            new PatternParameter { Key = DowntrendPct, Label = "累計跌幅至少 (%)", Default = 3, Step = 0.5, Max = 50, EnabledBy = RequireDowntrend },
        };

        public static IEnumerable<PatternParameter> VolumeParameters() => new[]
        {
            new PatternParameter { Key = RequireVolume, Label = "要求成交量放大", Kind = PatternParameterKind.Toggle, Default = 0 },
            new PatternParameter { Key = VolumeAvgDays, Label = "均量天數", Default = 5, Step = 1, Min = 1, Max = 60, IsInteger = true, EnabledBy = RequireVolume },
            new PatternParameter { Key = VolumeMultiplier, Label = "當天量 ≥ 均量 (倍)", Default = 1.5, Step = 0.1, Max = 20, EnabledBy = RequireVolume },
        };

        /// <summary>
        /// 先下跌：型態第一根 K 棒的收盤，相對它之前 N 天的收盤，跌幅至少 X%。
        /// </summary>
        public static bool PassesDowntrend(IReadOnlyList<StockDataPoint> candles, int patternStart, PatternParameterValues values)
        {
            if (!values.GetBool(RequireDowntrend)) return true;
            int days = values.GetInt(DowntrendDays);
            int baseIndex = patternStart - days;
            if (days < 1 || baseIndex < 0) return false;
            double baseClose = candles[baseIndex].Close;
            if (baseClose <= 0) return false;
            return (candles[patternStart].Close - baseClose) / baseClose * 100 <= -values.Get(DowntrendPct);
        }

        /// <summary>
        /// 成交量放大：型態成立日的成交量 ≥ 前 N 天平均量 × 倍數。
        /// </summary>
        public static bool PassesVolume(IReadOnlyList<StockDataPoint> candles, int index, PatternParameterValues values)
        {
            if (!values.GetBool(RequireVolume)) return true;
            int days = values.GetInt(VolumeAvgDays);
            int start = index - days;
            if (days < 1 || start < 0) return false;
            double avg = 0;
            for (int i = start; i < index; i++) avg += candles[i].Volume;
            avg /= days;
            return avg > 0 && candles[index].Volume >= avg * values.Get(VolumeMultiplier);
        }

        // ── 範例 K 線輔助 ──

        public const long BaseVolume = 1000;

        /// <summary>
        /// 產生型態前的下跌段 (每根黑 K，開盤 = 前一根收盤)。dailyDropPct 為每根跌幅 %。
        /// 根數 = max(下跌判斷天數, 均量天數, 5) + 1，確保「N 天前」與均量計算的範圍都存在。
        /// </summary>
        public static List<StockDataPoint> LeadingDecline(PatternParameterValues values, double dailyDropPct)
        {
            int count = Math.Max(Math.Max(values.GetInt(DowntrendDays), values.GetInt(VolumeAvgDays)), 5) + 1;
            var list = new List<StockDataPoint>();
            double price = 100;
            for (int i = 0; i < count; i++)
            {
                double close = price * (1 - dailyDropPct / 100);
                Add(list, price, close);
                price = close;
            }
            return list;
        }

        /// <summary>
        /// 下跌段每根的跌幅 %：要求先下跌時，讓 N 天累計跌幅略超過門檻；否則用溫和的下跌。
        /// </summary>
        public static double SuggestedDailyDropPct(PatternParameterValues values)
        {
            if (!values.GetBool(RequireDowntrend)) return 0.4;
            // 「N 天前」到型態第一根之間只有 N - 1 根下跌段；以複利反推每根跌幅，讓累計跌幅略超過門檻
            double target = Math.Min((values.Get(DowntrendPct) + 0.5) / 100, 0.95);
            int bars = Math.Max(values.GetInt(DowntrendDays) - 1, 1);
            return Math.Max((1 - Math.Pow(1 - target, 1.0 / bars)) * 100, 0.2);
        }

        /// <summary>
        /// 型態第一根的開盤價。下跌判斷天數為 1 時，比較基準就是前一根收盤，
        /// 下跌段完全不算數，因此改用跳空開低來達成跌幅門檻。
        /// </summary>
        public static double PatternStartOpen(List<StockDataPoint> leading, PatternParameterValues values)
        {
            double lastClose = leading[^1].Close;
            if (values.GetBool(RequireDowntrend) && values.GetInt(DowntrendDays) == 1)
                return lastClose * (1 - (values.Get(DowntrendPct) + 0.5) / 100);
            return lastClose;
        }

        // 型態成立日的範例成交量
        public static long SignalVolume(PatternParameterValues values) =>
            values.GetBool(RequireVolume) ? (long)Math.Ceiling(BaseVolume * values.Get(VolumeMultiplier) * 1.1) : BaseVolume * 13 / 10;

        // 型態後的上漲段
        public static void AppendRising(List<StockDataPoint> list, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double open = list[^1].Close;
                Add(list, open, open * 1.008);
            }
        }

        public static StockDataPoint Add(List<StockDataPoint> list, double open, double close, double? high = null, double? low = null, long volume = BaseVolume)
        {
            var c = new StockDataPoint
            {
                Date = (list.Count + 1).ToString(),
                Open = open,
                Close = close,
                High = high ?? Math.Max(open, close) * 1.003,
                Low = low ?? Math.Min(open, close) * 0.997,
                Volume = volume
            };
            list.Add(c);
            return c;
        }
    }
}
