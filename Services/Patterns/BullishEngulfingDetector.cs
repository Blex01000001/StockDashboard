using StockDashboard.Models;
using StockDashboard.Models.Patterns;

namespace StockDashboard.Services.Patterns
{
    /// <summary>
    /// 看漲吞沒判斷 (純計算，不碰資料庫)。
    /// 型態：前一天黑 K，當天紅 K 的實體把前一天的實體整個包住；另可要求先下跌、成交量放大、連影線吞沒。
    /// </summary>
    public static class BullishEngulfingDetector
    {
        /// <summary>
        /// candles 依日期由舊到新排序；判斷 index 那一天是否成立看漲吞沒。
        /// </summary>
        public static bool IsMatch(IReadOnlyList<StockDataPoint> candles, int index, BullishEngulfingOptions options)
        {
            if (index < 1 || index >= candles.Count) return false;

            var prev = candles[index - 1];
            var cur = candles[index];
            double tol = options.TolerancePct / 100;

            // 1. 前一天黑 K，且實體夠大
            double prevBody = prev.Open - prev.Close;
            if (prevBody <= 0 || prev.Open <= 0) return false;
            if (prevBody / prev.Open * 100 < options.MinPrevBodyPct) return false;

            // 2. 當天紅 K
            double curBody = cur.Close - cur.Open;
            if (curBody <= 0) return false;

            // 3. 實體吞沒：開低於 (或等於) 前一天收盤、收高於 (或等於) 前一天開盤
            if (cur.Open > prev.Close * (1 + tol)) return false;
            if (cur.Close < prev.Open * (1 - tol)) return false;

            // 4. 實體倍數
            if (curBody < prevBody * options.MinBodyRatio) return false;

            // 5. 連影線一起吞沒
            if (options.IncludeShadows)
            {
                if (cur.High < prev.High * (1 - tol)) return false;
                if (cur.Low > prev.Low * (1 + tol)) return false;
            }

            // 6. 先下跌：前一天收盤相對 N 天前收盤的跌幅
            if (options.RequireDowntrend)
            {
                int baseIndex = index - 1 - options.DowntrendDays;
                if (options.DowntrendDays < 1 || baseIndex < 0) return false;
                double baseClose = candles[baseIndex].Close;
                if (baseClose <= 0) return false;
                double changePct = (prev.Close - baseClose) / baseClose * 100;
                if (changePct > -options.DowntrendPct) return false;
            }

            // 7. 成交量放大：當天量 ≥ 前 N 天平均量 × 倍數
            if (options.RequireVolume)
            {
                int start = index - options.VolumeAvgDays;
                if (options.VolumeAvgDays < 1 || start < 0) return false;
                double avg = 0;
                for (int i = start; i < index; i++) avg += candles[i].Volume;
                avg /= options.VolumeAvgDays;
                if (avg <= 0 || cur.Volume < avg * options.VolumeMultiplier) return false;
            }

            return true;
        }
    }
}
