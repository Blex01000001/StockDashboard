namespace StockDashboard.Services.Patterns
{
    /// <summary>
    /// 技術指標計算 (純計算)。輸入一律為由舊到新的序列，輸出長度與輸入相同；資料不足的位置為 null。
    /// </summary>
    public static class TechnicalIndicators
    {
        /// <summary>
        /// 簡單移動平均 (SMA)，四捨五入到 decimals 位；前 period - 1 筆為 null。
        /// </summary>
        public static double?[] MovingAverage(IReadOnlyList<double> values, int period, int decimals = 2)
        {
            var result = new double?[values.Count];
            double sum = 0;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
                if (i >= period) sum -= values[i - period];
                if (i >= period - 1) result[i] = Math.Round(sum / period, decimals);
            }
            return result;
        }

        /// <summary>
        /// MACD (預設 12, 26, 9)，採台灣看盤軟體常見定義：
        /// DIF = EMA(收盤, fast) − EMA(收盤, slow)；MACD (訊號線) = EMA(DIF, signal)；OSC (柱狀) = DIF − MACD。
        /// EMA 以第一筆收盤為起始值，因此最前面幾十筆的數值較不準確 (完整歷史計算時影響不大)。
        /// </summary>
        public static (double?[] Dif, double?[] Signal, double?[] Histogram) Macd(
            IReadOnlyList<double> closes, int fast = 12, int slow = 26, int signal = 9, int decimals = 3)
        {
            int n = closes.Count;
            var dif = new double?[n];
            var dea = new double?[n];
            var osc = new double?[n];
            if (n == 0) return (dif, dea, osc);

            double kFast = 2.0 / (fast + 1), kSlow = 2.0 / (slow + 1), kSignal = 2.0 / (signal + 1);
            double emaFast = closes[0], emaSlow = closes[0], emaDif = 0;

            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    emaFast += (closes[i] - emaFast) * kFast;
                    emaSlow += (closes[i] - emaSlow) * kSlow;
                }
                double d = emaFast - emaSlow;
                emaDif = i == 0 ? d : emaDif + (d - emaDif) * kSignal;

                dif[i] = Math.Round(d, decimals);
                dea[i] = Math.Round(emaDif, decimals);
                osc[i] = Math.Round(d - emaDif, decimals);
            }
            return (dif, dea, osc);
        }
    }
}
