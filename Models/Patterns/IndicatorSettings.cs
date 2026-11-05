namespace StockDashboard.Models.Patterns
{
    /// <summary>
    /// 小卡附圖技術指標的參數 (預設為台灣看盤軟體常用值，頁面上可調整)。
    /// </summary>
    public class IndicatorSettings
    {
        public int MacdFast { get; set; } = 12;
        public int MacdSlow { get; set; } = 26;
        public int MacdSignal { get; set; } = 9;

        public int KdPeriod { get; set; } = 9;     // RSV 天數
        public int KdKSmooth { get; set; } = 3;    // K = 前一日 K × 2/3 + RSV × 1/3
        public int KdDSmooth { get; set; } = 3;    // D = 前一日 D × 2/3 + K × 1/3

        public int RsiFast { get; set; } = 6;
        public int RsiSlow { get; set; } = 12;

        // 布林通道 (畫在 K 線上，不是附圖；Bollinger 為是否顯示，關閉時不計算)
        public bool Bollinger { get; set; }
        public int BollPeriod { get; set; } = 20;
        public int BollMultiplier { get; set; } = 2;   // 標準差倍數

        public const int MaxPeriod = 200;
        public const int MaxBollMultiplier = 10;

        /// <summary>
        /// 檢查 MACD 參數；正確回傳 null，否則回傳說明文字。
        /// </summary>
        public string? ValidateMacd()
        {
            if (MacdFast < 1 || MacdSlow < 1 || MacdSignal < 1 || MacdFast > MaxPeriod || MacdSlow > MaxPeriod || MacdSignal > MaxPeriod)
                return $"MACD 參數需介於 1 ~ {MaxPeriod}";
            if (MacdFast >= MacdSlow)
                return "MACD 快線天數必須小於慢線天數";
            return null;
        }

        /// <summary>
        /// 檢查某個附圖 ("macd" / "kd" / "rsi") 的參數；正確回傳 null，否則回傳說明文字。
        /// </summary>
        public string? Validate(string subChart)
        {
            static bool InRange(params int[] values) => values.All(v => v >= 1 && v <= MaxPeriod);
            return subChart switch
            {
                "macd" => ValidateMacd(),
                "kd" => InRange(KdPeriod, KdKSmooth, KdDSmooth) ? null : $"KD 參數需介於 1 ~ {MaxPeriod}",
                "rsi" => !InRange(RsiFast, RsiSlow) ? $"RSI 參數需介於 1 ~ {MaxPeriod}"
                       : RsiFast >= RsiSlow ? "RSI 短週期必須小於長週期" : null,
                "boll" => BollPeriod < 1 || BollPeriod > MaxPeriod ? $"布林通道天數需介於 1 ~ {MaxPeriod}"
                        : BollMultiplier < 1 || BollMultiplier > MaxBollMultiplier ? $"布林通道倍數需介於 1 ~ {MaxBollMultiplier}" : null,
                _ => null
            };
        }

        public IndicatorSettings Clone() => (IndicatorSettings)MemberwiseClone();
    }
}
