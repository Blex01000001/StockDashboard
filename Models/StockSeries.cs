namespace StockDashboard.Models
{
    public class StockSeries
    {
        public string Name { get; set; } = string.Empty; // 圖例顯示名稱：「代號 名稱」或只有代號

        // 每個時間點的報酬率 (%)；該日無資料時為 null，序列化成 JSON 後即為 JS 的 null，ECharts 會在該點斷線
        public List<double?> Data { get; set; } = new();
    }
}
