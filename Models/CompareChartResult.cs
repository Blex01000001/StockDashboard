namespace StockDashboard.Models
{
    public class CompareChartResult
    {
        public List<string> Timeline { get; set; } = new(); // X軸：通用的日期陣列
        public List<StockSeries> SeriesData { get; set; } = new(); // 多檔股票各自的數據線
    }
}
