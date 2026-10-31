namespace StockDashboard.Models
{
    public class StockCombinationDto
    {
        public string DisplayName { get; set; } = string.Empty; // 選項顯示名稱 (選填)
        public string StockIds { get; set; } = string.Empty;    // 儲存的字串，例如 "006208,0050"
    }
}
