using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StockDashboard.Models.Patterns;
using StockDashboard.Services;
using ScanService = StockDashboard.Services.PatternScanService;
using StockDashboard.Services.Patterns;

namespace StockDashboard.Components.Pages
{
    public partial class PatternScan : ComponentBase, IAsyncDisposable
    {
        // 小卡範圍：型態成立日的前 / 後幾個交易日
        protected const int DaysBefore = 10;

        // 掃描中的過場至少顯示多久 (毫秒)，避免查詢太快時畫面一閃而過
        private const int MinLoadingMs = 400;
        protected static readonly int[] PageSizeOptions = { 5, 10, 20, 50 };

        [Inject] private IPatternScanService PatternScanService { get; set; } = default!;
        [Inject] private IEnumerable<ICandlePattern> PatternRegistrations { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        // 畫面狀態放在 Scoped 服務中：切換到其他頁再回來時保留 (見 PatternScanState)
        [Inject] protected PatternScanState State { get; set; } = default!;

        protected List<ICandlePattern> Patterns { get; private set; } = new();
        protected ICandlePattern Pattern => Patterns.FirstOrDefault(p => p.Key == State.PatternKey) ?? Patterns[0];
        protected PatternParameterValues Values => State.ValuesFor(Pattern);
        protected List<PatternParameterGroup> ParameterLayout => PatternParameterLayout.Build(Pattern.Parameters);
        protected bool IsScanning { get; private set; }

        // 目前輸入的觀察天數 (含 5 / 10 / 20)，以及依此決定的小卡「後 N 天」(標題即時顯示)
        protected List<int> Horizons => ScanService.DefaultHorizons.Concat(ScanService.ParseHorizons(State.ExtraHorizonsInput)).Distinct().OrderBy(d => d).ToList();
        protected int DaysAfter => ScanService.CardDaysAfter(Horizons);

        protected int TotalPages => State.Result == null ? 0 : (State.Result.Matches.Count + State.PageSize - 1) / State.PageSize;
        protected List<PatternMatch> PagedMatches =>
            State.Result?.Matches.Skip(State.PageIndex * State.PageSize).Take(State.PageSize).ToList() ?? new List<PatternMatch>();

        private IJSObjectReference? _jsModule;
        private bool _cardsNeedRender;
        private bool _previewNeedsRender;

        protected override void OnInitialized()
        {
            Patterns = PatternRegistrations.ToList();
            if (Patterns.All(p => p.Key != State.PatternKey)) State.PatternKey = Patterns[0].Key;
        }

        protected static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);

        // ── 參數 ──

        protected void OnPatternChanged(ChangeEventArgs e)
        {
            State.PatternKey = e.Value?.ToString() ?? Patterns[0].Key;
            _previewNeedsRender = true;
        }

        protected void OnNumberInput(PatternParameter parameter, ChangeEventArgs e)
        {
            // 輸入到一半 (例如空白) 無法解析時不更新，預覽維持上一個有效值
            if (Values.TrySetFromInput(parameter, e.Value?.ToString())) _previewNeedsRender = true;
        }

        protected void OnToggle(PatternParameter parameter, ChangeEventArgs e)
        {
            Values.Set(parameter.Key, e.Value is true);
            _previewNeedsRender = true;
        }

        protected void ResetValues()
        {
            State.ResetValues(Pattern);
            _previewNeedsRender = true;
        }

        // ── 掃描與分頁 ──

        protected async Task ScanAsync()
        {
            // 允許逗號、空白、頓號分隔
            var stockIds = State.StockIdsInput
                .Split(new[] { ',', '，', '、', ' ', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            if (stockIds.Count == 0)
            {
                State.Message = "請輸入至少一檔股票代號！";
                return;
            }

            // 先清掉舊的小卡 (含 ECharts 圖表)，顯示「掃描中」過場，再開始查詢
            IsScanning = true;
            State.Message = string.Empty;
            State.Result = null;
            await ClearCardChartsAsync();
            StateHasChanged();
            await Task.Delay(30);   // SQLite 查詢實際上是同步完成的，先讓出執行緒讓畫面更新
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var horizons = Horizons;
                var result = await PatternScanService.ScanAsync(stockIds, Pattern, Values.Clone(), DaysBefore,
                    ScanService.CardDaysAfter(horizons), horizons, State.Display.Indicators.Clone());

                int remaining = MinLoadingMs - (int)stopwatch.ElapsedMilliseconds;
                if (remaining > 0) await Task.Delay(remaining);

                State.Result = result;
                State.PageIndex = 0;
                _cardsNeedRender = true;

                if (State.Result.StocksWithoutData.Count > 0)
                {
                    State.Message = $"以下股票在資料庫中沒有日線資料，請先至「API 資料更新」同步：{string.Join("、", State.Result.StocksWithoutData)}";
                }
            }
            catch (Exception ex)
            {
                State.Result = null;
                State.Message = $"掃描失敗: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }
        }

        // 小卡 →「K線技術指標圖表」頁 (同一連線內切換，型態掃描結果仍保留)
        protected static string ChartLink(Models.Patterns.PatternMatch match) =>
            $"stock-chart?id={Uri.EscapeDataString(match.StockId)}&date={Uri.EscapeDataString(match.SignalDate)}";

        // ── 附圖、均線、指標參數 (ChartDisplayOptions 元件) ──

        // 指標參數改變時，用掃描時保留的完整日線立即重算小卡上的指標 (不查資料庫)
        protected void OnDisplayChanged(bool indicatorsChanged)
        {
            if (State.Result == null) return;
            if (indicatorsChanged) ScanService.RecomputeIndicators(State.Result, State.Display.Indicators);
            _cardsNeedRender = true;
        }

        private async Task ClearCardChartsAsync()
        {
            if (_jsModule == null) return;
            try { await _jsModule.InvokeVoidAsync("renderPatternCards", Array.Empty<object>(), Array.Empty<string>()); }
            catch (JSDisconnectedException) { }
        }

        protected void OnPageSizeChanged(ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out int size) && PageSizeOptions.Contains(size))
            {
                State.PageSize = size;
                State.PageIndex = 0;
                _cardsNeedRender = true;
            }
        }

        protected void GoToPage(int page)
        {
            if (page < 0 || page >= TotalPages) return;
            State.PageIndex = page;
            _cardsNeedRender = true;
        }

        // ── 圖表：<div> 要先由 Blazor 畫出來，ECharts 才能在裡面繪圖，所以在 OnAfterRender 呼叫 JS ──

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                // 從其他頁回來時，狀態中可能已有結果，要重畫小卡
                _previewNeedsRender = true;
                _cardsNeedRender = State.Result != null;
            }
            if (!_previewNeedsRender && !_cardsNeedRender) return;

            try
            {
                _jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", AssetVersion.Module("./Components/Pages/PatternScan.razor.js"));

                if (_previewNeedsRender)
                {
                    _previewNeedsRender = false;
                    var example = Pattern.BuildExample(Values);
                    await _jsModule.InvokeVoidAsync("renderPreview", "pattern-preview", example.Candles, example.SignalIndex, Pattern.BarCount);
                }

                if (_cardsNeedRender)
                {
                    _cardsNeedRender = false;
                    var cards = PagedMatches.Select((m, i) => new
                    {
                        containerId = $"pattern-chart-{i}",
                        signalIndex = m.SignalIndex,
                        barCount = m.PatternBarCount,
                        candles = m.Candles
                    });
                    await _jsModule.InvokeVoidAsync("renderPatternCards", cards, State.Display.VisibleMaNames, State.Result!.Horizons,
                        State.Display.OrderedSubCharts, State.Display.Indicators);
                }
            }
            catch (JSDisconnectedException)
            {
                // 連線已中斷 (關閉分頁)，不需處理
            }
            catch (Exception ex)
            {
                State.Message = $"圖表繪製失敗: {ex.Message}";
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
