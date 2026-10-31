using Microsoft.AspNetCore.Components;

namespace StockDashboard.Components.Pages
{
    public partial class StockQuery : ComponentBase
    {
        protected string TargetStockId { get; set; } = "";
        protected string Message { get; set; } = "";
        protected StockInfo StockResult { get; set; }

        protected void SearchStock()
        {
            Message = "";
            StockResult = null;

            if (TargetStockId == "2330")
            {
                StockResult = new StockInfo
                {
                    StockId = "2330",
                    StockName = "台積電",
                    ClosePrice = 950.00,
                    LastUpdated = DateTime.Now
                };
            }
            else
            {
                Message = "找不到資料";
            }
        }
    }

    public class StockInfo
    {
        public string StockId { get; set; }
        public string StockName { get; set; }
        public double ClosePrice { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}