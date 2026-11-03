using StockDashboard.Models.Patterns;

namespace StockDashboard.Services
{
    /// <summary>
    /// K 線圖的顯示設定：附圖、均線、指標參數。型態掃描與技術圖表兩頁共用 (各自一份)。
    /// </summary>
    public class ChartDisplaySettings
    {
        // 可畫的均線與預設是否顯示 (使用者指定預設：MA5 / 10 / 20 / 60 顯示，MA120 隱藏)
        public static readonly int[] MovingAveragePeriods = { 5, 10, 20, 60, 120 };
        public HashSet<int> VisibleMovingAverages { get; } = new() { 5, 10, 20, 60 };

        // 附圖：值 → 顯示名稱 (值也是傳給 JS 的識別字)。可複選，依此順序疊在 K 線下方；預設只有成交量 (使用者指定)
        public static readonly (string Value, string Label)[] SubChartOptions =
            {
                ("volume", "成交量"), ("macd", "MACD"), ("kd", "KD"), ("rsi", "RSI"),
                ("inst", "三大法人"), ("foreign", "外資"), ("trust", "投信"), ("dealer", "自營商"),
            };
        public HashSet<string> SubCharts { get; } = new() { "volume" };

        // 依 SubChartOptions 的順序排列已勾選的附圖
        public List<string> OrderedSubCharts => SubChartOptions.Select(o => o.Value).Where(SubCharts.Contains).ToList();

        // 傳給 JS 的均線名稱，例如 ["MA5", "MA20"]
        public string[] VisibleMaNames => VisibleMovingAverages.OrderBy(p => p).Select(p => $"MA{p}").ToArray();

        // 技術指標參數 (可在頁面調整，調整後用已載入的完整日線即時重算)
        public IndicatorSettings Indicators { get; set; } = new();
    }
}
