using StockDashboard.Models;
using StockDashboard.Models.Patterns;

namespace StockDashboard.Services.Patterns
{
    /// <summary>
    /// 紅三兵 / 黑三兵的共用邏輯 (兩者互為鏡像)：
    /// 連續三根同色 K 棒、實體夠大、收盤一根比一根高 (低)、開盤在前一根實體內、順勢方向的影線短。
    /// 預設參數由 Claude 依常見定義訂定，修改預設前先問使用者。
    /// </summary>
    public abstract class ThreeCandlesPattern : ICandlePattern
    {
        public const string MinBodyPct = "MinBodyPct";
        public const string RequireOpenInBody = "RequireOpenInBody";
        public const string MaxShadowRatio = "MaxShadowRatio";

        // +1：紅三兵 (三根紅 K 上漲)；-1：黑三兵 (三根黑 K 下跌)
        protected abstract int Sign { get; }

        // 型態出現前要求的趨勢 (紅三兵：先下跌；黑三兵：先上漲)
        protected abstract TrendKeys PriorTrend { get; }

        public abstract string Key { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public PatternDirection Direction => Sign > 0 ? PatternDirection.Bullish : PatternDirection.Bearish;
        public int BarCount => 3;

        private IReadOnlyList<PatternParameter>? _parameters;
        public IReadOnlyList<PatternParameter> Parameters => _parameters ??= new[]
        {
            new PatternParameter { Key = MinBodyPct, Label = "每根實體 ≥ (%)", Default = 1.0, Max = 20 },
            new PatternParameter { Key = RequireOpenInBody, Label = "開盤在前一根實體內", Kind = PatternParameterKind.Toggle, Default = 1 },
            new PatternParameter { Key = MaxShadowRatio, Label = $"{(Sign > 0 ? "上" : "下")}影線 ≤ 實體 (倍)", Default = 0.5, Step = 0.1, Max = 10 },
        }.Concat(PatternFilters.TrendParameters(PriorTrend, enabledByDefault: false)).Concat(PatternFilters.VolumeParameters()).ToList();

        public bool IsMatch(IReadOnlyList<StockDataPoint> candles, int index, PatternParameterValues values)
        {
            if (index < 2 || index >= candles.Count) return false;

            for (int i = index - 2; i <= index; i++)
            {
                var c = candles[i];

                // 1. 同色且實體夠大 (以 Sign 統一處理：紅三兵 Close > Open，黑三兵 Close < Open)
                double body = Sign * (c.Close - c.Open);
                if (body <= 0 || c.Open <= 0) return false;
                if (body / c.Open * 100 < values.Get(MinBodyPct)) return false;

                // 2. 順勢方向的影線短 (紅三兵看上影線，黑三兵看下影線)
                double shadow = Sign > 0 ? c.High - c.Close : c.Close - c.Low;
                if (shadow > body * values.Get(MaxShadowRatio)) return false;

                if (i == index - 2) continue;
                var prev = candles[i - 1];

                // 3. 收盤一根比一根高 (低)
                if (Sign * (c.Close - prev.Close) <= 0) return false;

                // 4. 開盤在前一根實體內 (含邊界)
                if (values.GetBool(RequireOpenInBody))
                {
                    double bodyLow = Math.Min(prev.Open, prev.Close);
                    double bodyHigh = Math.Max(prev.Open, prev.Close);
                    if (c.Open < bodyLow || c.Open > bodyHigh) return false;
                }
            }

            // 5、6. 共用條件：型態前的趨勢 (量到型態前一根，因為第一根就逆著趨勢)、成交量放大
            return PatternFilters.PassesTrend(candles, index - 3, values, PriorTrend)
                && PatternFilters.PassesVolume(candles, index, values);
        }

        public PatternExample BuildExample(PatternParameterValues values)
        {
            var list = PatternFilters.LeadingTrend(values, PriorTrend);
            double bodyPct = Math.Max(values.Get(MinBodyPct) * 1.2, 0.8) / 100;
            double shadowRatio = values.Get(MaxShadowRatio) * 0.5;
            bool openInBody = values.GetBool(RequireOpenInBody);

            double open = list[^1].Close;
            for (int k = 0; k < 3; k++)
            {
                if (k > 0)
                {
                    // 開在前一根實體的 6 成位置；不要求時開在前一根收盤
                    var prev = list[^1];
                    open = openInBody ? prev.Open + (prev.Close - prev.Open) * 0.6 : prev.Close;
                }
                double close = open * (1 + Sign * bodyPct);
                double body = Math.Abs(close - open);
                double high = Sign > 0 ? close + body * shadowRatio : open * 1.002;
                double low = Sign > 0 ? open * 0.998 : close - body * shadowRatio;
                PatternFilters.Add(list, open, close, high, low, volume: k == 2 ? PatternFilters.SignalVolume(values) : PatternFilters.BaseVolume);
            }

            int signal = list.Count - 1;
            PatternFilters.AppendTrend(list, 5, Sign);
            return new PatternExample { Candles = list, SignalIndex = signal };
        }
    }

    /// <summary>紅三兵：連續三根紅 K，收盤逐日墊高 (看多)</summary>
    public class ThreeWhiteSoldiersPattern : ThreeCandlesPattern
    {
        protected override int Sign => +1;
        protected override TrendKeys PriorTrend => PatternFilters.Down;
        public override string Key => "three-white-soldiers";
        public override string Name => "紅三兵";
        public override string Description => "連續三根紅K，收盤逐日墊高，上影線短";
    }

    /// <summary>黑三兵：連續三根黑 K，收盤逐日走低 (看空)</summary>
    public class ThreeBlackCrowsPattern : ThreeCandlesPattern
    {
        protected override int Sign => -1;
        protected override TrendKeys PriorTrend => PatternFilters.Up;
        public override string Key => "three-black-crows";
        public override string Name => "黑三兵";
        public override string Description => "連續三根黑K，收盤逐日走低，下影線短（看空型態，勝率以下跌計）";
    }
}
