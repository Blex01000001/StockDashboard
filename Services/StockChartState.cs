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
    }
}
