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

        // 小卡第一根 K 棒在該股完整日線中的位置 (重算技術指標時用)
        public int WindowStart { get; set; }

        // 型態前 N 天 + 當天 + 後 M 天的 K 線 (資料不足時較短)
        public List<PatternCandle> Candles { get; set; } = new();

        // 型態成立後第 N 個交易日的收盤報酬 % (Key = N，例如 5 / 10 / 20 / 使用者自訂天數)；資料不足為 null
        public Dictionary<int, double?> Returns { get; set; } = new();

        public double? ReturnAfter(int days) => Returns.TryGetValue(days, out var r) ? r : null;
    }

    /// <summary>
    /// 小卡上的單根 K 棒。傳到 JS 時屬性名會轉成 camelCase。
    /// </summary>
    public class PatternCandle
    {
        public static PatternCandle From(StockDataPoint c) =>
            new() { Date = c.Date, Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume };

        public string Date { get; set; } = string.Empty;
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public long Volume { get; set; }

        // 以完整歷史計算的均線 (不是只用小卡範圍)，前面天數不足時為 null
        public double? Ma5 { get; set; }
        public double? Ma10 { get; set; }
        public double? Ma20 { get; set; }
        public double? Ma60 { get; set; }
        public double? Ma120 { get; set; }

        // 成交量均線 (附圖：成交量)
        public double? VolMa5 { get; set; }
        public double? VolMa10 { get; set; }

        // MACD (12, 26, 9) (附圖：MACD)：DIF、MACD 訊號線、OSC 柱狀 = DIF − MACD
        public double? Dif { get; set; }
        public double? Macd { get; set; }
        public double? Osc { get; set; }

        // 布林通道上軌、下軌 (中軌 = MA20 型的均線，不另存)；未開啟布林通道時為 null
        public double? BollUpper { get; set; }
        public double? BollLower { get; set; }

        // KD (9, 3, 3) 與 RSI (6)、RSI (12)
        public double? K { get; set; }
        public double? D { get; set; }
        public double? RsiFast { get; set; }
        public double? RsiSlow { get; set; }

        // 三大法人買賣超 (股；正為買超)。外資 = 外陸資 + 外資自營商；尚未同步或當天無資料為 null
        public long? TotalNet { get; set; }
        public long? ForeignNet { get; set; }
        public long? TrustNet { get; set; }
        public long? DealerNet { get; set; }

        // 全體外資及陸資持股比率 (%)
        public double? ForeignHoldingPct { get; set; }

        // Brent 原油價格 (美元 / 桶，附圖：原油)：當天之前最近一個交易日的當日均價；沒有資料為 null
        public double? Oil { get; set; }

        // 美國費半 (SOX) K 線 (附圖：費半)：對齊到這個台股交易日的美股行情 (見 PatternScanService.ApplyMarketIndex)；
        // 台股連假期間的多個美股交易日已合併成一根。SoxDate = 這根最後一個美股交易日。沒有資料為 null
        public double? SoxOpen { get; set; }
        public double? SoxHigh { get; set; }
        public double? SoxLow { get; set; }
        public double? SoxClose { get; set; }
        public string? SoxDate { get; set; }
    }

    /// <summary>
    /// 一次掃描的結果
    /// </summary>
    public class PatternScanResult
    {
        // 掃描的型態名稱 (顯示用)
        public string PatternName { get; set; } = string.Empty;

        // true：看多型態 (之後上漲算勝)；false：看空型態 (之後下跌算勝)
        public bool IsBullish { get; set; } = true;

        // 小卡顯示型態成立日前 / 後幾天
        public int DaysBefore { get; set; }
        public int DaysAfter { get; set; }

        // 這次掃描計算的觀察天數 (例如 5, 10, 20, 60)，由小到大
        public List<int> Horizons { get; set; } = new();

        // 各觀察天數的勝率 (順序同 Horizons)
        public List<PatternWinRate> WinRates { get; set; } = new();

        // 依型態日期由新到舊排序
        public List<PatternMatch> Matches { get; set; } = new();

        // 資料庫中查無日線資料的股票代號 (頁面提示使用者先同步)
        public List<string> StocksWithoutData { get; set; } = new();

        // 各股完整日線 (由舊到新)：調整 MACD 等參數時，不必重新查資料庫就能重算小卡上的指標
        public Dictionary<string, List<StockDataPoint>> FullCandles { get; set; } = new();
    }

    /// <summary>
    /// N 日勝率：之後第 N 日報酬方向符合型態 (看多為正、看空為負) 的比例。
    /// 之後天數不足 (報酬為 null) 的結果不計入分母。
    /// </summary>
    public class PatternWinRate
    {
        public int Days { get; set; }
        public int Wins { get; set; }
        public int Total { get; set; }

        // 百分比 (0~100)；沒有可計算的樣本時為 null
        public double? Rate => Total == 0 ? null : Math.Round(Wins * 100.0 / Total, 1);

        // 醒目程度：0 = 未滿 50%；1 = 50% 以上，之後每多 5 個百分點加一級，最高 5 級 (70% 以上)
        public int HighlightLevel => Rate is double r && r >= 50 ? Math.Min((int)((r - 50) / 5) + 1, 5) : 0;
    }
}
