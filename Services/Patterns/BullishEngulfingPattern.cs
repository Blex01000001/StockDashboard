using StockDashboard.Models;
using StockDashboard.Models.Patterns;

namespace StockDashboard.Services.Patterns
{
    /// <summary>
    /// 看漲吞沒：前一天黑 K，當天紅 K 的實體把前一天的實體整個包住。
    /// 預設參數為與使用者討論後的版本，修改預設前先問使用者。
    /// </summary>
    public class BullishEngulfingPattern : ICandlePattern
    {
        public const string MinPrevBodyPct = "MinPrevBodyPct";
        public const string MinBodyRatio = "MinBodyRatio";
        public const string TolerancePct = "TolerancePct";
        public const string IncludeShadows = "IncludeShadows";

        public string Key => "bullish-engulfing";
        public string Name => "看漲吞沒";
        public string Description => "前一天黑K，當天紅K實體完全包住前一天實體";
        public PatternDirection Direction => PatternDirection.Bullish;
        public int BarCount => 2;

        public IReadOnlyList<PatternParameter> Parameters { get; } = new[]
        {
            new PatternParameter { Key = MinPrevBodyPct, Label = "前一天黑K實體 ≥ (%)", Default = 0.5, Max = 20 },
            new PatternParameter { Key = MinBodyRatio, Label = "當天實體 ≥ 前一天 (倍)", Default = 1.0, Max = 20 },
            new PatternParameter { Key = TolerancePct, Label = "吞沒容許誤差 (%)", Default = 0, Max = 5 },
            new PatternParameter { Key = IncludeShadows, Label = "影線也要吞沒", Kind = PatternParameterKind.Toggle, Default = 0 },
        }.Concat(PatternFilters.DowntrendParameters()).Concat(PatternFilters.VolumeParameters()).ToList();

        public bool IsMatch(IReadOnlyList<StockDataPoint> candles, int index, PatternParameterValues values)
        {
            if (index < 1 || index >= candles.Count) return false;

            var prev = candles[index - 1];
            var cur = candles[index];
            double tol = values.Get(TolerancePct) / 100;

            // 1. 前一天黑 K，且實體夠大
            double prevBody = prev.Open - prev.Close;
            if (prevBody <= 0 || prev.Open <= 0) return false;
            if (prevBody / prev.Open * 100 < values.Get(MinPrevBodyPct)) return false;

            // 2. 當天紅 K
            double curBody = cur.Close - cur.Open;
            if (curBody <= 0) return false;

            // 3. 實體吞沒：開低於 (或等於) 前一天收盤、收高於 (或等於) 前一天開盤
            if (cur.Open > prev.Close * (1 + tol)) return false;
            if (cur.Close < prev.Open * (1 - tol)) return false;

            // 4. 實體倍數
            if (curBody < prevBody * values.Get(MinBodyRatio)) return false;

            // 5. 連影線一起吞沒
            if (values.GetBool(IncludeShadows))
            {
                if (cur.High < prev.High * (1 - tol)) return false;
                if (cur.Low > prev.Low * (1 + tol)) return false;
            }

            // 6、7. 共用條件：先下跌 (以前一天黑 K 為型態起點)、成交量放大
            return PatternFilters.PassesDowntrend(candles, index - 1, values)
                && PatternFilters.PassesVolume(candles, index, values);
        }

        public PatternExample BuildExample(PatternParameterValues values)
        {
            var list = PatternFilters.LeadingDecline(values);
            double tol = values.Get(TolerancePct) / 100;

            // 前一天黑 K：實體略大於門檻 (門檻為 0 時仍畫出看得見的實體)
            double prevOpen = PatternFilters.PatternStartOpen(list, values, PatternFilters.Down);
            double prevClose = prevOpen * (1 - Math.Max(values.Get(MinPrevBodyPct) * 1.1, 0.3) / 100);
            var prev = PatternFilters.Add(list, prevOpen, prevClose, high: prevOpen * 1.004, low: prevClose * 0.996);

            // 當天紅 K：開在前一天收盤附近 (有容許誤差時略高)，實體達到倍數且收過前一天開盤
            double prevBody = prevOpen - prevClose;
            double curOpen = prevClose * (1 + tol * 0.8) - prevClose * 0.002;
            double curBody = Math.Max(prevBody * values.Get(MinBodyRatio) * 1.05, prevOpen * (1 - tol) - curOpen + prevOpen * 0.001);
            double curClose = curOpen + curBody;
            bool shadows = values.GetBool(IncludeShadows);
            PatternFilters.Add(list, curOpen, curClose,
                high: shadows ? Math.Max(curClose * 1.002, prev.High * 1.001) : curClose * 1.002,
                low: shadows ? Math.Min(curOpen * 0.998, prev.Low * 0.999) : curOpen * 0.998,
                volume: PatternFilters.SignalVolume(values));

            int signal = list.Count - 1;
            PatternFilters.AppendRising(list, 5);
            return new PatternExample { Candles = list, SignalIndex = signal };
        }
    }
}
