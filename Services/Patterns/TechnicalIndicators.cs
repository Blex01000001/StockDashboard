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

        /// <summary>
        /// KD 隨機指標 (台灣常用定義，預設 9, 3, 3)：
        /// RSV = (今日收盤 − 最近 n 日最低) / (最近 n 日最高 − 最近 n 日最低) × 100；
        /// K = 前一日 K × (kSmooth − 1)/kSmooth + RSV / kSmooth；D 同理以 K 平滑；K、D 起始值為 50。
        /// 前 n − 1 筆為 null；最高 = 最低時 RSV 視為 50。
        /// </summary>
        public static (double?[] K, double?[] D) Kd(
            IReadOnlyList<double> highs, IReadOnlyList<double> lows, IReadOnlyList<double> closes,
            int period = 9, int kSmooth = 3, int dSmooth = 3, int decimals = 2)
        {
            int n = closes.Count;
            var kOut = new double?[n];
            var dOut = new double?[n];
            double k = 50, d = 50;
            for (int i = period - 1; i < n; i++)
            {
                double highest = double.MinValue, lowest = double.MaxValue;
                for (int j = i - period + 1; j <= i; j++)
                {
                    highest = Math.Max(highest, highs[j]);
                    lowest = Math.Min(lowest, lows[j]);
                }
                double rsv = highest > lowest ? (closes[i] - lowest) / (highest - lowest) * 100 : 50;
                k = (k * (kSmooth - 1) + rsv) / kSmooth;
                d = (d * (dSmooth - 1) + k) / dSmooth;
                kOut[i] = Math.Round(k, decimals);
                dOut[i] = Math.Round(d, decimals);
            }
            return (kOut, dOut);
        }

        /// <summary>
        /// RSI (Wilder 平滑)：第 period 筆起有值。
        /// 初始平均漲幅 / 跌幅 = 前 period 次漲跌的簡單平均；之後 平均 = (前一平均 × (period − 1) + 本次) / period。
        /// RSI = 100 − 100 / (1 + 平均漲幅 / 平均跌幅)；平均跌幅為 0 時為 100，漲跌都為 0 時為 50。
        /// </summary>
        public static double?[] Rsi(IReadOnlyList<double> closes, int period, int decimals = 2)
        {
            int n = closes.Count;
            var result = new double?[n];
            if (period < 1 || n <= period) return result;

            double avgGain = 0, avgLoss = 0;
            for (int i = 1; i <= period; i++)
            {
                double change = closes[i] - closes[i - 1];
                if (change > 0) avgGain += change; else avgLoss -= change;
            }
            avgGain /= period;
            avgLoss /= period;
            result[period] = Math.Round(RsiValue(avgGain, avgLoss), decimals);

            for (int i = period + 1; i < n; i++)
            {
                double change = closes[i] - closes[i - 1];
                avgGain = (avgGain * (period - 1) + Math.Max(change, 0)) / period;
                avgLoss = (avgLoss * (period - 1) + Math.Max(-change, 0)) / period;
                result[i] = Math.Round(RsiValue(avgGain, avgLoss), decimals);
            }
            return result;
        }

        private static double RsiValue(double avgGain, double avgLoss) =>
            avgLoss == 0 ? (avgGain == 0 ? 50 : 100) : 100 - 100 / (1 + avgGain / avgLoss);
    }
}
