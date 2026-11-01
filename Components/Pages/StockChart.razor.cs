using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Components.Pages
{
    public partial class StockChart : ComponentBase, IAsyncDisposable
    {
        // K 線圖顯示的最近交易日筆數
        private const int CandleCount = 220;

        [Inject] private IStockPriceRepository PriceRepository { get; set; } = default!;
        [Inject] private IStockListRepository StockListRepository { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        public string TargetStockId { get; set; } = "2330";
        public string Message { get; set; } = string.Empty;
        private bool _isLoading = false;
        private IJSObjectReference? _jsModule;

        public async Task LoadChartData()
        {
            if (string.IsNullOrWhiteSpace(TargetStockId))
            {
                Message = "請輸入有效的股票代號！";
                return;
            }
            if (_isLoading) return;

            try
            {
                _isLoading = true;
                Message = "資料載入中...";

                string stockId = TargetStockId.Trim();
                var candles = (await PriceRepository.GetLatestCandlesAsync(stockId, CandleCount)).ToList();

                if (candles.Count == 0)
                {
                    Message = $"找不到代號 {stockId} 的股票資料。";
                    return;
                }

                Message = string.Empty;

                _jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./Components/Pages/StockChart.razor.js");

                // 圖表標題顯示「代號 名稱」，例如 "2330 台積電"；清單中沒有該股時只顯示代號
                string? stockName = await StockListRepository.GetStockNameOrNullAsync(stockId);
                string chartLabel = stockName == null ? stockId : $"{stockId} {stockName}";

                await _jsModule.InvokeVoidAsync("renderStockChart", "chart-container", chartLabel, candles);
            }
            catch (Exception ex)
            {
                Message = $"圖表渲染失敗或資料庫讀取失敗: {ex.Message}";
            }
            finally
            {
                _isLoading = false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_jsModule != null)
            {
                try
                {
                    await _jsModule.DisposeAsync();
                }
                catch (JSDisconnectedException)
                {
                    // 使用者直接關閉分頁時，SignalR 連線已中斷，JS 端物件已不存在，忽略即可
                }
            }
        }
    }
}
