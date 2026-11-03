using System.Text.Json;

namespace StockDashboard.Models
{
    /// <summary>
    /// 證交所「fields + data」格式的回應 (T86、MI_QFIIS 等)。
    /// data 的儲存格可能是字串或數字 (MI_QFIIS 的比率欄位是 JSON 數字)，所以用 JsonElement 接。
    /// </summary>
    public class TwseFieldTableResponse
    {
        public string Stat { get; set; } = string.Empty;
        public string? Date { get; set; }
        public List<string> Fields { get; set; } = new();
        public List<List<JsonElement>> Data { get; set; } = new();
    }
}
