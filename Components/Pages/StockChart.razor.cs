using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StockDashboard.Services;

namespace StockDashboard.Components.Pages
{
    public partial class StockChart : ComponentBase, IAsyncDisposable
    {
        // K 線圖顯示的最近交易日筆數 (均線與指標仍以完整歷史計算)
        private const int CandleCount = 220;
        private const string ContainerId = "chart-container";

        [Inject] private IStockChartService ChartService { get; set; } = default!;
        [Inject] private StockChartState State { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        public string Message { get; set; } = string.Empty;
        private bool _isLoading = false;
        private bool _chartNeedsRender;
        private IJSObjectReference? _jsModule;

        protected override void OnInitialized()
        {
            // 從其他頁面切回來時，重畫上次的圖表
            _chartNeedsRender = State.Data != null;
        }

        public async Task LoadChartData()
        {
            if (string.IsNullOrWhiteSpace(State.TargetStockId))
            {
                Message = "請輸入有效的股票代號！";
                return;
            }
            if (_isLoading) return;

            try
            {
                _isLoading = true;
                Message = "資料載入中...";

                string stockId = State.TargetStockId.Trim();
                var data = await ChartService.LoadAsync(stockId, CandleCount, State.Display.Indicators);
                if (data == null)
                {
                    Message = $"找不到代號 {stockId} 的股票資料。";
                    return;
                }

                Message = string.Empty;
                State.Data = data;
                _chartNeedsRender = true;
            }
            catch (Exception ex)
            {
                Message = $"資料庫讀取失敗: {ex.Message}";
            }
            finally
            {
                _isLoading = false;
            }
        }

        // 勾選附圖 / 均線、調整指標參數：用已載入的完整日線重算後重畫，不查資料庫
        protected void OnDisplayChanged(bool indicatorsChanged)
        {
            if (State.Data == null) return;
            if (indicatorsChanged) StockChartService.Recompute(State.Data, State.Display.Indicators);
            _chartNeedsRender = true;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!_chartNeedsRender || State.Data == null) return;
            _chartNeedsRender = false;

            try
            {
                _jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", AssetVersion.Module("./Components/Pages/StockChart.razor.js"));
                await _jsModule.InvokeVoidAsync("renderStockChart", ContainerId, State.Data.Label, State.Data.Candles,
                    State.Display.VisibleMaNames, State.Display.OrderedSubCharts, State.Display.Indicators);
            }
            catch (JSDisconnectedException)
            {
                // 連線已中斷 (分頁關閉)，不必再畫
            }
            catch (Exception ex)
            {
                Message = $"圖表渲染失敗: {ex.Message}";
                StateHasChanged();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_jsModule != null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("disposeChart", ContainerId);
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
