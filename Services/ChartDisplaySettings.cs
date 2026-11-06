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
                ("oil", "原油"), ("sox", "費半"),
            };
        public HashSet<string> SubCharts { get; } = new() { "volume" };

        // 附圖的排列順序 (使用者可在頁面拖曳調整)：預設 = SubChartOptions 的順序；包含所有附圖，不論有沒有勾選，
        // 這樣取消勾選再勾回來時還在原本的位置
        public static readonly string[] DefaultSubChartOrder = SubChartOptions.Select(o => o.Value).ToArray();
        public List<string> SubChartOrder { get; } = DefaultSubChartOrder.ToList();

        // 依 SubChartOrder 排列已勾選的附圖 (由上到下疊在 K 線下方)
        public List<string> OrderedSubCharts => SubChartOrder.Where(SubCharts.Contains).ToList();

        public bool IsDefaultSubChartOrder => SubChartOrder.SequenceEqual(DefaultSubChartOrder);

        /// <summary>
        /// 把附圖 dragged 移到 target 目前的位置 (target 及其後的項目順延)，用於拖曳排序。
        /// 往右拖會排在 target 之後、往左拖排在 target 之前。代號不認得或相同時不動，回傳是否有改變。
        /// </summary>
        public bool MoveSubChart(string dragged, string target)
        {
            int from = SubChartOrder.IndexOf(dragged), to = SubChartOrder.IndexOf(target);
            if (from < 0 || to < 0 || from == to) return false;
            SubChartOrder.RemoveAt(from);
            SubChartOrder.Insert(to, dragged);
            return true;
        }

        public void ResetSubChartOrder()
        {
            SubChartOrder.Clear();
            SubChartOrder.AddRange(DefaultSubChartOrder);
        }

        // 傳給 JS 的均線名稱，例如 ["MA5", "MA20"]
        public string[] VisibleMaNames => VisibleMovingAverages.OrderBy(p => p).Select(p => $"MA{p}").ToArray();

        // 技術指標參數 (可在頁面調整，調整後用已載入的完整日線即時重算)
        public IndicatorSettings Indicators { get; set; } = new();
    }
}
