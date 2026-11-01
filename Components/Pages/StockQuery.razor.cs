using Microsoft.AspNetCore.Components;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Components.Pages
{
    public partial class StockQuery : ComponentBase
    {
        [Inject] private IStockPriceRepository PriceRepository { get; set; } = default!;
        [Inject] private IStockListRepository StockListRepository { get; set; } = default!;

        protected string TargetStockId { get; set; } = "";
        protected string Message { get; set; } = "";

        // 查詢結果：資料庫中該股票「最新一筆」日線資料；null 代表尚未查詢或查無資料
        protected StockDailyPriceEntity? StockResult { get; set; }

        // 股票名稱 (來自 StockList 資料表)；清單中沒有時為 null
        protected string? StockName { get; set; }

        protected async Task SearchStock()
        {
            Message = "";
            StockResult = null;
            StockName = null;

            if (string.IsNullOrWhiteSpace(TargetStockId))
            {
                Message = "請輸入股票代號";
                return;
            }

            try
            {
                string stockId = TargetStockId.Trim();
                StockResult = await PriceRepository.GetLatestPriceAsync(stockId);

                if (StockResult == null)
                {
                    Message = "找不到資料，請先至「API 資料更新」頁面同步該股票。";
                    return;
                }

                StockName = await StockListRepository.GetStockNameOrNullAsync(stockId);
            }
            catch (Exception ex)
            {
                Message = $"資料庫讀取失敗: {ex.Message}";
            }
        }
    }
}
