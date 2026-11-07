using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StockDashboard.Services;

namespace StockDashboard.Components.Pages
{
    public partial class StockChart : ComponentBase, IAsyncDisposable
    {
        private const string ContainerId = "chart-container";

        // 從型態掃描小卡開啟：/stock-chart?id=2330&date=2026-03-30
        [SupplyParameterFromQuery(Name = "id")] public string? QueryStockId { get; set; }
        [SupplyParameterFromQuery(Name = "date")] public string? QueryDate { get; set; }
        [SupplyParameterFromQuery(Name = "adj")] public string? QueryAdjusted { get; set; }   // 1 = 該次掃描用還原股價

        [Inject] private IStockChartService ChartService { get; set; } = default!;
        [Inject] private StockChartState State { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        public string Message { get; set; } = string.Empty;
        private bool _isLoading = false;
        private bool _chartNeedsRender;
        private IJSObjectReference? _jsModule;

        protected override async Task OnInitializedAsync()
        {
            if (!string.IsNullOrWhiteSpace(QueryStockId))
            {
                State.TargetStockId = QueryStockId.Trim();
                State.Adjusted = QueryAdjusted == "1";
                await LoadAsync(QueryDate);
                return;
            }
            // 從其他頁面切回來時，重畫上次的圖表
            _chartNeedsRender = State.Data != null;
        }

        // 按「繪製圖表」：手動查詢，不再標示型態日
        public Task LoadChartData() => LoadAsync(null);

        // 切換還原股價：完整日線會變，要重新載入 (保留標示的型態日)
        protected Task OnAdjustedChanged(ChangeEventArgs e)
        {
            State.Adjusted = e.Value is true;
            return State.Data == null ? Task.CompletedTask : LoadAsync(State.FocusDate);
        }

        // 改顯示筆數：用已載入的完整日線重切範圍，不查資料庫
        protected void OnCountChanged(ChangeEventArgs e)
        {
            if (!int.TryParse(e.Value?.ToString(), out int count)) return;
            State.CandleCount = count;
            if (State.Data == null) return;
            StockChartService.SetWindow(State.Data, count, State.Display.Indicators);
            _chartNeedsRender = true;
        }

        private async Task LoadAsync(string? focusDate)
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
                var data = await ChartService.LoadAsync(stockId, State.CandleCount, State.Display.Indicators, State.Adjusted);
                if (data == null)
                {
                    Message = $"找不到代號 {stockId} 的股票資料。";
                    return;
                }

                // 標示的日期若不在顯示範圍內，自動改用能包含它的筆數選項
                State.FocusDate = focusDate;
                if (focusDate != null)
                {
                    int count = StockChartService.CountToInclude(data.FullCandles, focusDate,
                        StockChartState.CountOptions.Select(o => o.Value), State.CandleCount);
                    if (count != State.CandleCount)
                    {
                        State.CandleCount = count;
                        StockChartService.SetWindow(data, count, State.Display.Indicators);
                    }
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
                    State.Display.VisibleMaNames, State.Display.OrderedSubCharts, State.Display.Indicators, State.FocusDate);
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
