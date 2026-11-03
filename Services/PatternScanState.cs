using StockDashboard.Models.Patterns;
using StockDashboard.Services.Patterns;

namespace StockDashboard.Services
{
    /// <summary>
    /// 型態掃描頁的畫面狀態。註冊為 Scoped：Blazor Server 中每個瀏覽器分頁 (連線) 一份，
    /// 切換到其他頁面再回來時元件會重建，但狀態留在這裡，所以輸入、參數與結果都會保留。
    /// 重新整理瀏覽器 (F5) 會建立新連線，狀態會重置。
    /// </summary>
    public class PatternScanState
    {
        public string StockIdsInput { get; set; } = "2330,0050,006208";
        public string PatternKey { get; set; } = string.Empty;
        public int PageSize { get; set; } = 10;

        // 除了 5 / 10 / 20 日之外，額外計算報酬與勝率的天數 (使用者輸入原文，例如 "60,120")
        public string ExtraHorizonsInput { get; set; } = string.Empty;

        // 小卡可畫的均線與預設是否顯示 (使用者指定預設：MA5 / 10 / 20 / 60 顯示，MA120 隱藏)
        public static readonly int[] MovingAveragePeriods = { 5, 10, 20, 60, 120 };
        public HashSet<int> VisibleMovingAverages { get; } = new() { 5, 10, 20, 60 };

        // 小卡附圖：值 → 顯示名稱 (值也是傳給 JS 的識別字)。可複選，依此順序疊在 K 線下方；預設只有成交量 (使用者指定)
        public static readonly (string Value, string Label)[] SubChartOptions =
            {
                ("volume", "成交量"), ("macd", "MACD"), ("kd", "KD"), ("rsi", "RSI"),
                ("inst", "三大法人"), ("foreign", "外資"), ("trust", "投信"), ("dealer", "自營商"),
            };
        public HashSet<string> SubCharts { get; } = new() { "volume" };

        // 依 SubChartOptions 的順序排列已勾選的附圖
        public List<string> OrderedSubCharts => SubChartOptions.Select(o => o.Value).Where(SubCharts.Contains).ToList();

        // 技術指標參數 (MACD 可在頁面調整，調整後用保留的完整日線即時重算)
        public IndicatorSettings Indicators { get; set; } = new();
        public int PageIndex { get; set; }
        public string Message { get; set; } = string.Empty;
        public PatternScanResult? Result { get; set; }

        // 每種型態各自保留參數，切換型態再切回來不會遺失
        private readonly Dictionary<string, PatternParameterValues> _valuesByPattern = new();

        public PatternParameterValues ValuesFor(ICandlePattern pattern)
        {
            if (!_valuesByPattern.TryGetValue(pattern.Key, out var values))
            {
                values = PatternParameterValues.FromDefaults(pattern.Parameters);
                _valuesByPattern[pattern.Key] = values;
            }
            return values;
        }

        public void ResetValues(ICandlePattern pattern) =>
            _valuesByPattern[pattern.Key] = PatternParameterValues.FromDefaults(pattern.Parameters);
    }
}
