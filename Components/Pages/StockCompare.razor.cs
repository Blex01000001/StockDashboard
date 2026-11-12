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

        [Inject]
        private IPriceSyncService PriceSync { get; set; } = default!;

        // 預設股票代號：使用者可用「設為預設」存在瀏覽器 (localStorage)；沒存過用 FallbackIds
        private const string DefaultIdsKey = "stockCompare.defaultIds";
        private const string FallbackIds = "006208,00981A,00991A";

        // 「更新並比較」每檔更新的月數
        private const int UpdateMonths = 3;

        private CancellationTokenSource? _updateCts;

        // 前端繫結變數
        public string StockIdsInput { get; set; } = FallbackIds;
        public int SelectedDays { get; set; } = 20;
        public string Message { get; set; } = string.Empty;
        public bool IsBusy { get; private set; }
        private bool _isLoading = false;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;
            try
            {
                _jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", AssetVersion.Module("./Components/Pages/StockCompare.razor.js"));
                string saved = await _jsModule.InvokeAsync<string>("loadDefaultIds", DefaultIdsKey);
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    StockIdsInput = saved;
                    StateHasChanged();
                }
            }
            catch (Exception)
            {
                // 讀不到預設 (例如連線中斷) 就用內建預設，不影響比較功能
            }
        }

        // 把目前輸入的股票代號存成預設 (下次開啟頁面時自動帶入)
        public async Task SaveDefaultIds()
        {
            var ids = StockIdParser.Parse(StockIdsInput);
            if (ids.Count == 0)
            {
                Message = "請先輸入至少一檔股票代號再設為預設！";
                return;
            }
            try
            {
                _jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", AssetVersion.Module("./Components/Pages/StockCompare.razor.js"));
                bool ok = await _jsModule.InvokeAsync<bool>("saveDefaultIds", DefaultIdsKey, string.Join(",", ids));
                Message = ok ? string.Empty : "瀏覽器不允許儲存設定 (可能是無痕模式或封鎖網站資料)。";
                if (ok) StockIdsInput = string.Join(",", ids);
            }
            catch (Exception ex)
            {
                Message = $"儲存預設失敗: {ex.Message}";
            }
        }

        // 先更新目前股票代號近三個月的日線 (等同「日線批次同步」)，再用最新資料比較
        public async Task UpdateAndCompare()
        {
            if (IsBusy) return;
            var stockIds = StockIdParser.Parse(StockIdsInput);
            if (stockIds.Count == 0)
            {
                Message = "請輸入至少一檔股票代號！";
                return;
            }

            IsBusy = true;
            _updateCts = new CancellationTokenSource();
            string syncNote = string.Empty;
            try
            {
                var progress = new Progress<PriceSyncProgress>(p =>
                {
                    Message = $"更新日線 [第 {p.StockIndex}/{p.StockTotal} 檔 {p.StockId}] 正在下載 {p.Month:yyyy年MM月}...";
                    InvokeAsync(StateHasChanged);
                });
                var multi = await PriceSync.SyncManyAsync(stockIds, UpdateMonths, progress, _updateCts.Token);
                if (multi.StoppedReason != null) syncNote = $"日線更新中斷（{multi.StoppedReason}），已改用資料庫現有資料比較。";
                else if (multi.Cancelled) syncNote = "日線更新已取消，已改用資料庫現有資料比較。";
            }
            catch (Exception ex)
            {
                syncNote = $"日線更新失敗（{ex.Message}），已改用資料庫現有資料比較。";
            }
            finally
            {
                _updateCts.Dispose();
                _updateCts = null;
            }

            try
            {
                await CompareStocks();
            }
            finally
            {
                IsBusy = false;
            }
            if (syncNote.Length > 0) Message = string.IsNullOrEmpty(Message) ? syncNote : $"{syncNote} {Message}";
        }

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

                var stockIds = StockIdParser.Parse(StockIdsInput);

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
            _updateCts?.Cancel();
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