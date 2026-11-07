namespace StockDashboard.Services
{
    /// <summary>
    /// 技術圖表頁的畫面狀態 (Scoped，同型態掃描頁)：切換到其他頁面再回來時，代號、附圖設定與圖表都保留。
    /// </summary>
    public class StockChartState
    {
        public string TargetStockId { get; set; } = "2330";
        public ChartDisplaySettings Display { get; } = new();
        public StockChartData? Data { get; set; }

        // 用還原股價 (預設原始價)；從型態掃描小卡開啟時依該次掃描決定
        public bool Adjusted { get; set; }

        // 顯示筆數 (最近 N 根；0 = 全部)，預設一年
        public static readonly (int Value, string Label)[] CountOptions = { (120, "半年"), (250, "一年"), (750, "三年"), (0, "全部") };
        public int CandleCount { get; set; } = 250;

        // 從型態掃描小卡跳過來時標示的日期 (圖上畫線並縮放到附近)；手動查詢其他股票時清除
        public string? FocusDate { get; set; }
    }
}
