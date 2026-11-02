using StockDashboard.Models;
using StockDashboard.Models.Patterns;

namespace StockDashboard.Services.Patterns
{
    /// <summary>
    /// 趨勢條件的參數鍵與方向。Sign = -1 為「先下跌」，+1 為「先上漲」。
    /// </summary>
    public sealed record TrendKeys(string Require, string Days, string Pct, int Sign, string Verb, string Noun);

    /// <summary>
    /// 多種型態共用的條件 (先下跌 / 先上漲、成交量放大) 與產生範例 K 線的輔助方法。
    /// </summary>
    public static class PatternFilters
    {
        public const string RequireDowntrend = "RequireDowntrend";
        public const string DowntrendDays = "DowntrendDays";
        public const string DowntrendPct = "DowntrendPct";
        public const string RequireUptrend = "RequireUptrend";
        public const string UptrendDays = "UptrendDays";
        public const string UptrendPct = "UptrendPct";
        public const string RequireVolume = "RequireVolume";
        public const string VolumeAvgDays = "VolumeAvgDays";
        public const string VolumeMultiplier = "VolumeMultiplier";

        public static readonly TrendKeys Down = new(RequireDowntrend, DowntrendDays, DowntrendPct, -1, "下跌", "跌幅");
        public static readonly TrendKeys Up = new(RequireUptrend, UptrendDays, UptrendPct, +1, "上漲", "漲幅");

        public static IEnumerable<PatternParameter> TrendParameters(TrendKeys trend, bool enabledByDefault) => new[]
        {
            new PatternParameter { Key = trend.Require, Label = $"要求先{trend.Verb}", Kind = PatternParameterKind.Toggle, Default = enabledByDefault ? 1 : 0 },
            new PatternParameter { Key = trend.Days, Label = $"{trend.Verb}判斷天數", Default = 5, Step = 1, Min = 1, Max = 60, IsInteger = true, EnabledBy = trend.Require },
            new PatternParameter { Key = trend.Pct, Label = $"累計{trend.Noun}至少 (%)", Default = 3, Step = 0.5, Max = 50, EnabledBy = trend.Require },
        };

        public static IEnumerable<PatternParameter> DowntrendParameters(bool enabledByDefault = true) => TrendParameters(Down, enabledByDefault);
        public static IEnumerable<PatternParameter> UptrendParameters(bool enabledByDefault = true) => TrendParameters(Up, enabledByDefault);

        public static IEnumerable<PatternParameter> VolumeParameters() => new[]
        {
            new PatternParameter { Key = RequireVolume, Label = "要求成交量放大", Kind = PatternParameterKind.Toggle, Default = 0 },
            new PatternParameter { Key = VolumeAvgDays, Label = "均量天數", Default = 5, Step = 1, Min = 1, Max = 60, IsInteger = true, EnabledBy = RequireVolume },
            new PatternParameter { Key = VolumeMultiplier, Label = "當天量 ≥ 均量 (倍)", Default = 1.5, Step = 0.1, Max = 20, EnabledBy = RequireVolume },
        };

        /// <summary>
        /// 趨勢條件：第 trendEnd 根的收盤相對它之前 N 根的收盤，跌幅 (或漲幅) 至少 X%。
        /// trendEnd 由各型態決定：第一根順著趨勢的型態 (吞沒、晨星) 用型態第一根；
        /// 第一根逆著趨勢的型態 (紅三兵、黑三兵) 用型態前一根。
        /// </summary>
        public static bool PassesTrend(IReadOnlyList<StockDataPoint> candles, int trendEnd, PatternParameterValues values, TrendKeys trend)
        {
            if (!values.GetBool(trend.Require)) return true;
            int days = values.GetInt(trend.Days);
            int baseIndex = trendEnd - days;
            if (days < 1 || baseIndex < 0 || trendEnd >= candles.Count) return false;
            double baseClose = candles[baseIndex].Close;
            if (baseClose <= 0) return false;
            double changePct = (candles[trendEnd].Close - baseClose) / baseClose * 100;
            return trend.Sign < 0 ? changePct <= -values.Get(trend.Pct) : changePct >= values.Get(trend.Pct);
        }

        public static bool PassesDowntrend(IReadOnlyList<StockDataPoint> candles, int patternStart, PatternParameterValues values) =>
            PassesTrend(candles, patternStart, values, Down);

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
        /// 產生型態前的趨勢段 (順著趨勢方向的 K 棒，開盤 = 前一根收盤)。
        /// 根數 = max(趨勢判斷天數, 均量天數, 5) + 1，確保「N 天前」與均量計算的範圍都存在。
        /// 每根漲跌幅以複利反推，讓 N - 1 根的累計幅度略超過門檻 (型態第一根若順勢，會再加大幅度)。
        /// </summary>
        public static List<StockDataPoint> LeadingTrend(PatternParameterValues values, TrendKeys trend)
        {
            int days = values.GetInt(trend.Days);
            int count = Math.Max(Math.Max(days, values.GetInt(VolumeAvgDays)), 5) + 1;

            double movePct = 0.4;   // 不要求趨勢時的溫和走勢
            if (values.GetBool(trend.Require))
            {
                double target = Math.Min((values.Get(trend.Pct) + 0.5) / 100, 0.95);
                int bars = Math.Max(days - 1, 1);
                movePct = trend.Sign < 0
                    ? Math.Max((1 - Math.Pow(1 - target, 1.0 / bars)) * 100, 0.2)
                    : Math.Max((Math.Pow(1 + target, 1.0 / bars) - 1) * 100, 0.2);
            }

            var list = new List<StockDataPoint>();
            double price = 100;
            for (int i = 0; i < count; i++)
            {
                double close = price * (1 + trend.Sign * movePct / 100);
                Add(list, price, close);
                price = close;
            }
            return list;
        }

        public static List<StockDataPoint> LeadingDecline(PatternParameterValues values) => LeadingTrend(values, Down);

        /// <summary>
        /// 型態第一根的開盤價 (第一根順勢的型態用)。趨勢判斷天數為 1 時，比較基準就是前一根收盤，
        /// 趨勢段完全不算數，因此改用跳空來達成幅度門檻。
        /// </summary>
        public static double PatternStartOpen(List<StockDataPoint> leading, PatternParameterValues values, TrendKeys trend)
        {
            double lastClose = leading[^1].Close;
            if (values.GetBool(trend.Require) && values.GetInt(trend.Days) == 1)
                return lastClose * (1 + trend.Sign * (values.Get(trend.Pct) + 0.5) / 100);
            return lastClose;
        }

        // 型態成立日的範例成交量
        public static long SignalVolume(PatternParameterValues values) =>
            values.GetBool(RequireVolume) ? (long)Math.Ceiling(BaseVolume * values.Get(VolumeMultiplier) * 1.1) : BaseVolume * 13 / 10;

        // 型態後的走勢 (sign = +1 上漲、-1 下跌)
        public static void AppendTrend(List<StockDataPoint> list, int count, int sign = +1)
        {
            for (int i = 0; i < count; i++)
            {
                double open = list[^1].Close;
                Add(list, open, open * (1 + sign * 0.008));
            }
        }

        public static void AppendRising(List<StockDataPoint> list, int count) => AppendTrend(list, count, +1);

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
