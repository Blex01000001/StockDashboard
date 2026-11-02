namespace StockDashboard.Models.Patterns
{
    /// <summary>
    /// 一筆符合型態的結果 (一張小卡)
    /// </summary>
    public class PatternMatch
    {
        public string StockId { get; set; } = string.Empty;

        // 圖卡標題用的名稱；查無時為 null
        public string? StockName { get; set; }

        // 型態成立的那一天 (yyyy-MM-dd)
        public string SignalDate { get; set; } = string.Empty;

        // 型態成立那天 (型態最後一根 K 棒) 在 Candles 中的索引 (前面資料不足時會較小)
        public int SignalIndex { get; set; }

        // 型態由幾根 K 棒組成 (看漲吞沒 2、晨星 3)，小卡依此標示型態範圍
        public int PatternBarCount { get; set; } = 1;

        // 型態前 N 天 + 當天 + 後 M 天的 K 線 (資料不足時較短)
        public List<PatternCandle> Candles { get; set; } = new();

        // 型態成立後第 5 / 10 / 20 個交易日的收盤報酬 %；資料不足為 null
        public double? Return5 { get; set; }
        public double? Return10 { get; set; }
        public double? Return20 { get; set; }
    }

    /// <summary>
    /// 小卡上的單根 K 棒。傳到 JS 時屬性名會轉成 camelCase。
    /// </summary>
    public class PatternCandle
    {
        public string Date { get; set; } = string.Empty;
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public long Volume { get; set; }

        // 以完整歷史計算的均線 (不是只用小卡範圍)，前面天數不足時為 null
        public double? Ma5 { get; set; }
        public double? Ma20 { get; set; }
    }

    /// <summary>
    /// 一次掃描的結果
    /// </summary>
    public class PatternScanResult
    {
        // 掃描的型態名稱 (顯示用)
        public string PatternName { get; set; } = string.Empty;

        // 依型態日期由新到舊排序
        public List<PatternMatch> Matches { get; set; } = new();

        // 資料庫中查無日線資料的股票代號 (頁面提示使用者先同步)
        public List<string> StocksWithoutData { get; set; } = new();
    }
}
