using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StockDashboard.Services;

namespace StockDashboard.Components.Pages
{
    public partial class StockCompare : ComponentBase, IAsyncDisposable
    {
        // 1. 透過屬性注入 Service 層與 JSRuntime
        [Inject]
        private IStockPerformanceService CompareService { get; set; } = default!;

        [Inject]
        private IJSRuntime JSRuntime { get; set; } = default!;

        private IJSObjectReference? _jsModule;

        // 前端繫結變數
        public string StockIdsInput { get; set; } = "006208,00981A,00991A";
        public int SelectedDays { get; set; } = 20;
        public string Message { get; set; } = string.Empty;
        private bool _isLoading = false;

        public async Task CompareStocks()
        {
            if (string.IsNullOrWhiteSpace(StockIdsInput))
            {
                Message = "請輸入至少一檔股票代號！";
                return;
            }
            if (_isLoading) return;

            try
            {
                _isLoading = true;
                Message = "計算報酬率中...";

                var stockIds = StockIdsInput.Split(',')
                                            .Select(s => s.Trim().ToUpper())
                                            .Where(s => !string.IsNullOrEmpty(s))
                                            .ToList();

                if (!stockIds.Any())
                {
                    Message = "請輸入正確的股票代號格式。";
                    return;
                }

                // 2. 將核心運算交給 Service，UI 層只負責接收結果
                var chartData = await CompareService.GetCompareChartDataAsync(stockIds, SelectedDays);

                if (chartData == null || !chartData.SeriesData.Any())
                {
                    Message = "資料庫中找不到對應股票的歷史數據，請先前往 API 頁面同步資料。";
                    return;
                }

                Message = string.Empty;

                // 3. 呼叫專屬 JavaScript 渲染多線圖
                _jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", AssetVersion.Module("./Components/Pages/StockCompare.razor.js"));

                await _jsModule.InvokeVoidAsync("renderCompareChart", "compare-chart-container", chartData);
            }
            catch (Exception ex)
            {
                Message = $"圖表對比失敗: {ex.Message}";
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