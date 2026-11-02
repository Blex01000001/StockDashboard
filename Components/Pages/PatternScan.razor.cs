using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StockDashboard.Models.Patterns;
using StockDashboard.Services;

namespace StockDashboard.Components.Pages
{
    public partial class PatternScan : ComponentBase, IAsyncDisposable
    {
        // 小卡範圍：型態當天的前 / 後幾個交易日；每頁張數
        protected const int DaysBefore = 10;
        protected const int DaysAfter = 20;
        private const int PageSize = 10;

        [Inject] private IPatternScanService PatternScanService { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        public string StockIdsInput { get; set; } = "2330,0050,006208";
        public BullishEngulfingOptions Options { get; set; } = new();
        public string Message { get; set; } = string.Empty;
        public bool IsScanning { get; set; }

        public PatternScanResult? Result { get; set; }
        public int PageIndex { get; set; }
        public int TotalPages => Result == null ? 0 : (Result.Matches.Count + PageSize - 1) / PageSize;
        public List<PatternMatch> PagedMatches =>
            Result?.Matches.Skip(PageIndex * PageSize).Take(PageSize).ToList() ?? new List<PatternMatch>();

        private IJSObjectReference? _jsModule;
        private bool _chartsNeedRender;

        protected void ResetOptions() => Options = new BullishEngulfingOptions();

        protected async Task ScanAsync()
        {
            // 允許逗號、空白、頓號分隔
            var stockIds = StockIdsInput
                .Split(new[] { ',', '，', '、', ' ', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            if (stockIds.Count == 0)
            {
                Message = "請輸入至少一檔股票代號！";
                return;
            }

            IsScanning = true;
            Message = "掃描中...";
            try
            {
                Result = await PatternScanService.ScanBullishEngulfingAsync(stockIds, Options, DaysBefore, DaysAfter);
                PageIndex = 0;
                _chartsNeedRender = true;

                Message = Result.StocksWithoutData.Count > 0
                    ? $"以下股票在資料庫中沒有日線資料，請先至「API 資料更新」同步：{string.Join("、", Result.StocksWithoutData)}"
                    : string.Empty;
            }
            catch (Exception ex)
            {
                Result = null;
                Message = $"掃描失敗: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }
        }

        protected void GoToPage(int page)
        {
            if (page < 0 || page >= TotalPages) return;
            PageIndex = page;
            _chartsNeedRender = true;
        }

        // 小卡的 <div> 要先由 Blazor 畫出來，才能讓 ECharts 在裡面繪圖，所以在 OnAfterRender 呼叫 JS
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!_chartsNeedRender || Result == null) return;
            _chartsNeedRender = false;

            try
            {
                _jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./Components/Pages/PatternScan.razor.js");

                var cards = PagedMatches.Select((m, i) => new
                {
                    containerId = $"pattern-chart-{i}",
                    signalIndex = m.SignalIndex,
                    candles = m.Candles
                });
                await _jsModule.InvokeVoidAsync("renderPatternCards", cards);
            }
            catch (Exception ex)
            {
                Message = $"圖表繪製失敗: {ex.Message}";
                StateHasChanged();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_jsModule != null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("disposeAll");
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
