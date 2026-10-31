namespace StockDashboard.Models
{
    public class StockSeries
    {
        public string Name { get; set; } = string.Empty; // 股票代號

        // ✨ 修正此處：改為 object，這樣 C# 的 null 轉成 JSON 時才會變成 JS 的 null
        public List<object> Data { get; set; } = new();
    }
}
