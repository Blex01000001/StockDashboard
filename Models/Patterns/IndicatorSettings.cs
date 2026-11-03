namespace StockDashboard.Models.Patterns
{
    /// <summary>
    /// 小卡附圖技術指標的參數。MACD 可在頁面上調整；KD、RSI 目前固定為台灣看盤軟體常用值。
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

        public const int MaxPeriod = 200;

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

        public IndicatorSettings Clone() => (IndicatorSettings)MemberwiseClone();
    }
}
