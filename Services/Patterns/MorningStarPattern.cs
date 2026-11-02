using StockDashboard.Models;
using StockDashboard.Models.Patterns;

namespace StockDashboard.Services.Patterns
{
    /// <summary>
    /// 晨星 (三根 K 棒)：
    /// 第一天長黑 K → 第二天小實體 (星線，實體下緣低於第一天收盤) → 第三天紅 K 收復第一天實體一定比例。
    /// 預設參數由 Claude 依常見定義訂定，修改預設前先問使用者。
    /// </summary>
    public class MorningStarPattern : ICandlePattern
    {
        public const string Day1MinBodyPct = "Day1MinBodyPct";
        public const string StarMaxBodyRatio = "StarMaxBodyRatio";
        public const string RequireGap = "RequireGap";
        public const string Day3MinRecoveryPct = "Day3MinRecoveryPct";

        public string Key => "morning-star";
        public string Name => "晨星";
        public string Description => "長黑K → 小實體星線 → 紅K收復長黑K實體一定比例";
        public int BarCount => 3;

        public IReadOnlyList<PatternParameter> Parameters { get; } = new[]
        {
            new PatternParameter { Key = Day1MinBodyPct, Label = "第一天黑K實體 ≥ (%)", Default = 1.5, Max = 20 },
            new PatternParameter { Key = StarMaxBodyRatio, Label = "星線實體 ≤ 第一天 (倍)", Default = 0.3, Step = 0.05, Max = 1 },
            new PatternParameter { Key = RequireGap, Label = "星線實體跳空低於第一天", Kind = PatternParameterKind.Toggle, Default = 0 },
            new PatternParameter { Key = Day3MinRecoveryPct, Label = "第三天收復第一天實體 ≥ (%)", Default = 50, Step = 5, Max = 200 },
        }.Concat(PatternFilters.DowntrendParameters()).Concat(PatternFilters.VolumeParameters()).ToList();

        public bool IsMatch(IReadOnlyList<StockDataPoint> candles, int index, PatternParameterValues values)
        {
            if (index < 2 || index >= candles.Count) return false;

            var d1 = candles[index - 2];
            var star = candles[index - 1];
            var d3 = candles[index];

            // 1. 第一天長黑 K
            double body1 = d1.Open - d1.Close;
            if (body1 <= 0 || d1.Open <= 0) return false;
            if (body1 / d1.Open * 100 < values.Get(Day1MinBodyPct)) return false;

            // 2. 星線：實體小，且實體下緣低於第一天收盤 (位置在低檔)
            double starTop = Math.Max(star.Open, star.Close);
            double starBottom = Math.Min(star.Open, star.Close);
            if (starTop - starBottom > body1 * values.Get(StarMaxBodyRatio)) return false;
            if (starBottom >= d1.Close) return false;
            if (values.GetBool(RequireGap) && starTop >= d1.Close) return false;

            // 3. 第三天紅 K，收盤收復第一天實體的指定比例
            if (d3.Close <= d3.Open) return false;
            if (d3.Close < d1.Close + body1 * values.Get(Day3MinRecoveryPct) / 100) return false;

            // 4、5. 共用條件：先下跌 (以第一天為型態起點)、成交量放大
            return PatternFilters.PassesDowntrend(candles, index - 2, values)
                && PatternFilters.PassesVolume(candles, index, values);
        }

        public PatternExample BuildExample(PatternParameterValues values)
        {
            var list = PatternFilters.LeadingDecline(values, PatternFilters.SuggestedDailyDropPct(values));

            // 第一天長黑 K
            double d1Open = PatternFilters.PatternStartOpen(list, values);
            double d1Close = d1Open * (1 - Math.Max(values.Get(Day1MinBodyPct) * 1.1, 0.8) / 100);
            double body1 = d1Open - d1Close;
            PatternFilters.Add(list, d1Open, d1Close);

            // 星線：實體為允許上限的 6 成；要求跳空時整根實體在第一天收盤之下
            double starBody = body1 * values.Get(StarMaxBodyRatio) * 0.6;
            double starTop = values.GetBool(RequireGap) ? d1Close * 0.996 : d1Close * 0.999;
            double starBottom = starTop - starBody;
            if (starBottom >= d1Close) starBottom = d1Close * 0.998;
            PatternFilters.Add(list, starTop, starBottom);   // 小黑 K (開在上緣、收在下緣)

            // 第三天紅 K：收盤略超過收復門檻
            double d3Open = starTop * 1.002;
            double d3Close = Math.Max(d1Close + body1 * values.Get(Day3MinRecoveryPct) / 100 * 1.05, d3Open * 1.003);
            PatternFilters.Add(list, d3Open, d3Close, volume: PatternFilters.SignalVolume(values));

            int signal = list.Count - 1;
            PatternFilters.AppendRising(list, 5);
            return new PatternExample { Candles = list, SignalIndex = signal };
        }
    }
}
