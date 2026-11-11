using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StockDashboard.Components.Shared;
using StockDashboard.Models;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;
using StockDashboard.Services;

namespace StockDashboard.Components.Pages
{
    public partial class StockUpdate : ComponentBase, IDisposable
    {
        [Inject] private ITwseApiClient TwseApi { get; set; } = default!;
        [Inject] private IPriceSyncService PriceSync { get; set; } = default!;
        [Inject] private IStockNameRepository StockNameRepository { get; set; } = default!;
        [Inject] private IInstitutionalRepository InstitutionalRepository { get; set; } = default!;
        [Inject] private IInstitutionalSyncService InstitutionalSync { get; set; } = default!;
        [Inject] private IFinMindSyncService FinMindSync { get; set; } = default!;
        [Inject] private IStockCorporateActionSyncService ActionSync { get; set; } = default!;
        [Inject] private IStockCorporateActionRepository ActionRepository { get; set; } = default!;
        [Inject] private IOilPriceSyncService OilSync { get; set; } = default!;
        [Inject] private IOilPriceRepository OilRepository { get; set; } = default!;
        [Inject] private ILocalPathOpener PathOpener { get; set; } = default!;
        [Inject] private IMarketIndexSyncService MarketIndexSync { get; set; } = default!;
        [Inject] private IMarketIndexRepository MarketIndexRepository { get; set; } = default!;
        [Inject] private IYahooOilSyncService YahooOilSync { get; set; } = default!;
        [Inject] private IIndustryPriceSyncRunner IndustryRunner { get; set; } = default!;
        [Inject] private IIndustryRepository IndustryRepository { get; set; } = default!;
        [Inject] private IJSRuntime JS { get; set; } = default!;

        public string BatchStockId { get; set; } = "2330";
        public string StatusMessage { get; set; } = string.Empty;
        public bool IsError { get; set; } = false;
        public bool IsProcessing { get; set; } = false;
        public string SelectedRange { get; set; } = "1";

        // ── 依產業別批次同步日線 (背景執行者是單例：離開頁面也繼續跑，回來時顯示目前進度) ──
        public string IndustryCodeInput { get; set; } = string.Empty;
        public List<IndustryOption> Industries { get; private set; } = new();   // 讀取失敗或沒有產業別欄位時為空

        public string IndustryStatusMessage => IndustryMessage();
        public bool IndustryIsError => !IndustryRunner.IsRunning && IndustryRunner.LastResult?.Error != null;

        // ── 股票清單同步 ──
        public StockNameSummary? NameSummary { get; set; }
        public bool IsSyncingList { get; set; } = false;
        public string ListStatusMessage { get; set; } = string.Empty;
        public bool ListIsError { get; set; } = false;
        private StockIdDataList? _stockIdDataList;

        // ── 三大法人同步 ──
        public static readonly (string Value, string Label)[] InstitutionalRangeOptions =
        {
            ("7", "最近一週"),
            ("1m", "最近一個月"),
        };
        public string InstitutionalRange { get; set; } = "7";
        public (string? FirstDate, string? LastDate, int Days)? InstitutionalSummary { get; set; }
        public bool IsSyncingInstitutional { get; set; } = false;
        public string InstitutionalStatusMessage { get; set; } = string.Empty;
        public bool InstitutionalIsError { get; set; } = false;
        private CancellationTokenSource? _institutionalCts;

        // ── FinMind 個股三大法人同步 ──
        public string FinMindStockIds { get; set; } = string.Empty;
        public string FinMindIndustry { get; set; } = string.Empty;
        public bool FinMindFullReload { get; set; } = false;
        public int FinMindSyncedCount { get; set; }
        public bool IsSyncingFinMind { get; set; } = false;
        public string FinMindStatusMessage { get; set; } = string.Empty;
        public bool FinMindIsError { get; set; } = false;
        private CancellationTokenSource? _finMindCts;
        private CancellationTokenSource? _actionCts;
        private CancellationTokenSource? _priceCts;

        // ── FinMind 除權除息 (還原股價用) ──
        public string ActionStockIds { get; set; } = string.Empty;
        public string ActionIndustry { get; set; } = string.Empty;
        public (int Stocks, int Events)? ActionSummary { get; set; }
        public bool IsSyncingAction { get; set; } = false;
        public string ActionStatusMessage { get; set; } = string.Empty;
        public bool ActionIsError { get; set; } = false;

        // ── 原油價格同步 ──
        public (string? FirstDate, string? LastDate, int Days)? OilSummary { get; set; }
        public bool IsSyncingOil { get; set; } = false;
        public string OilStatusMessage { get; set; } = string.Empty;
        public bool OilIsError { get; set; } = false;

        // ── Yahoo Brent 期貨 (BZ=F) 同步 ──
        public (string? FirstDate, string? LastDate, int Days)? YahooOilSummary { get; set; }
        public bool IsSyncingYahooOil { get; set; } = false;
        public string YahooOilStatusMessage { get; set; } = string.Empty;
        public bool YahooOilIsError { get; set; } = false;

        // ── 費半 (SOX) 同步 ──
        public (string? FirstDate, string? LastDate, int Days)? SoxSummary { get; set; }
        public bool IsSyncingSox { get; set; } = false;
        public string SoxStatusMessage { get; set; } = string.Empty;
        public bool SoxIsError { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            IndustryRunner.Changed += OnIndustryChanged;
            try { Industries = await IndustryRepository.GetIndustriesAsync(); }
            catch (Exception) { Industries = new(); }
            if (!string.IsNullOrEmpty(IndustryRunner.IndustryCode)) IndustryCodeInput = IndustryRunner.IndustryCode;
            await LoadNameSummaryAsync();
            await LoadInstitutionalSummaryAsync();
            await LoadOilSummaryAsync();
            await LoadActionSummaryAsync();
            await LoadSoxSummaryAsync();
            await LoadYahooOilSummaryAsync();
        }

        private void OnIndustryChanged() => InvokeAsync(StateHasChanged);

        public void StartIndustrySync()
        {
            if (string.IsNullOrWhiteSpace(IndustryCodeInput)) return;
            IndustryRunner.Start(IndustryCodeInput);
        }

        // 只更新近三個月：日常補最新資料用 (已完整的月份不重抓，所以通常很快)
        public void StartIndustryRecentSync()
        {
            if (string.IsNullOrWhiteSpace(IndustryCodeInput)) return;
            IndustryRunner.Start(IndustryCodeInput, RecentMonths);
        }

        private const int RecentMonths = 3;
        private string IndustryScope => IndustryRunner.Months < IndustryPriceSyncService.MaxMonths ? $"（近 {IndustryRunner.Months} 個月）" : "";

        // 停止前先跟使用者確認 (整批同步要跑好幾個小時，誤按很可惜)；已抓到的資料都已存入，之後再按「開始同步」會接著跑
        public async Task StopIndustrySync()
        {
            if (await ConfirmStopAsync("依產業別的批次同步", "之後再按「開始同步」會接著跑")) IndustryRunner.Stop();
        }

        // 所有「停止」按鈕共用的確認視窗；連線中斷等無法詢問時一律不停止
        private async Task<bool> ConfirmStopAsync(string what, string note)
        {
            try { return await JS.InvokeAsync<bool>("confirm", $"確定要停止{what}嗎？\n\n已抓到的資料都已存入，{note}。"); }
            catch (Exception) { return false; }
        }

        private string IndustryMessage()
        {
            var p = IndustryRunner.Progress;
            var r = IndustryRunner.LastResult;
            if (IndustryRunner.IsRunning)
            {
                return p == null
                    ? $"產業別 {IndustryRunner.IndustryCode}{IndustryScope}：準備中..."
                    : $"產業別 {p.IndustryCode}{IndustryScope}：第 {p.StockIndex}/{p.StockTotal} 檔 {p.StockId} — {p.Message}（已請求 {p.Requests} 次、寫入 {p.RowsWritten} 筆）";
            }
            if (r == null) return string.Empty;

            string summary = $"完成 {r.StocksDone}/{r.StocksTotal} 檔、寫入 {r.RowsWritten} 筆，向證交所請求 {r.Requests} 次。";
            if (r.Error != null) return $"產業別 {IndustryRunner.IndustryCode}{IndustryScope} 同步中斷：{r.Error}。{(r.StocksTotal > 0 ? "已抓到的資料都已存入，稍後再按一次即可接著跑。" : "")}{(r.StocksTotal > 0 ? summary : "")}";
            if (r.Cancelled) return $"產業別 {IndustryRunner.IndustryCode}{IndustryScope} 已停止。{summary}再按一次會接著跑。";
            return $"產業別 {IndustryRunner.IndustryCode}{IndustryScope} 同步完成。{summary}";
        }

        public void Dispose()
        {
            IndustryRunner.Changed -= OnIndustryChanged;   // 只取消訂閱，背景同步繼續跑
            // 離開頁面時停止同步 (同步中的頁面元件已銷毀，進度無處顯示)
            _institutionalCts?.Cancel();
            _finMindCts?.Cancel();
            _actionCts?.Cancel();
            _priceCts?.Cancel();
        }

        private async Task LoadInstitutionalSummaryAsync()
        {
            try
            {
                InstitutionalSummary = await InstitutionalRepository.GetSummaryAsync();
                FinMindSyncedCount = (await InstitutionalRepository.GetFinMindSyncedAsync(FinMindSyncService.TradingDataset)).Count;
            }
            catch (Exception ex)
            {
                SetInstitutionalStatus($"讀取三大法人資料失敗: {ex.Message}", true);
            }
        }

        // 舊資料庫沒有這張表時只是不顯示摘要，不影響其他功能
        private async Task LoadActionSummaryAsync()
        {
            try { ActionSummary = await ActionRepository.GetSummaryAsync(); }
            catch (Exception) { ActionSummary = null; }
        }

        /// <summary>用 FinMind 逐檔下載除權除息結果表 (還原股價用)；IP 被封鎖或達上限時立即停止</summary>
        // 依產業別：把該產業全部股票代號帶入輸入框，再照原本的流程同步
        public async Task SyncActionsByIndustry()
        {
            if (IsSyncingAction) return;
            var ids = await LoadIndustryIdsAsync(ActionIndustry, m => SetActionStatus(m, true));
            if (ids == null) return;
            ActionStockIds = string.Join(",", ids);
            await SyncActions();
        }

        // 讀某產業別的股票代號；沒選或找不到時用 report 顯示原因並回傳 null
        private async Task<List<string>?> LoadIndustryIdsAsync(string industryCode, Action<string> report)
        {
            if (string.IsNullOrWhiteSpace(industryCode)) { report("請先選擇產業別！"); return null; }
            try
            {
                var ids = await IndustryRepository.GetStockIdsByIndustryAsync(industryCode);
                if (ids.Count == 0) { report($"StockList 找不到產業別 {industryCode} 的股票"); return null; }
                return ids;
            }
            catch (Exception ex)
            {
                report($"讀取產業別失敗: {ex.Message}");
                return null;
            }
        }

        public async Task SyncActions()
        {
            if (IsSyncingAction) return;

            var stockIds = ActionStockIds
                .Split(new[] { ',', '，', '、', ' ', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();
            if (stockIds.Count == 0)
            {
                SetActionStatus("請輸入至少一檔股票代號！", true);
                return;
            }

            IsSyncingAction = true;
            _actionCts = new CancellationTokenSource();
            SetActionStatus($"準備同步 {stockIds.Count} 檔...", false);
            var progress = new SyncProgress<StockCorporateActionSyncProgress>(p => SetActionStatus($"[{p.Done}/{p.Total}] {p.Message}", false));

            try
            {
                var result = await ActionSync.SyncAsync(stockIds, progress, _actionCts.Token);
                await LoadActionSummaryAsync();

                string summary = $"完成 {result.StocksDone}/{stockIds.Count} 檔、寫入 {result.RowsWritten} 筆（{result.StocksWithoutEvents} 檔沒有除權息資料），向 FinMind 請求 {result.Requests} 次。";
                if (result.Error != null)
                {
                    string retry = result.Blocked ? "" : "已完成的股票都已存入，稍後再按一次同步即可。";
                    SetActionStatus($"同步中斷：{result.Error}。{retry}{summary}", true);
                }
                else if (result.Cancelled)
                {
                    SetActionStatus($"已停止。{summary}", false);
                }
                else
                {
                    SetActionStatus($"除權除息同步完成。{summary}", false);
                }
            }
            catch (Exception ex)
            {
                SetActionStatus($"除權除息同步失敗: {ex.Message}", true);
            }
            finally
            {
                IsSyncingAction = false;
                _actionCts.Dispose();
                _actionCts = null;
                await InvokeAsync(StateHasChanged);
            }
        }

        public async Task StopActionSync()
        {
            if (!await ConfirmStopAsync("除權除息同步", "再按一次同步即可接著跑")) return;
            _actionCts?.Cancel();
            SetActionStatus("正在停止...", false);
        }

        private void SetActionStatus(string msg, bool isError)
        {
            ActionStatusMessage = msg;
            ActionIsError = isError;
            InvokeAsync(StateHasChanged);
        }

        // 舊資料庫沒有這張表時只是不顯示摘要，不影響其他功能
        private async Task LoadOilSummaryAsync()
        {
            try { OilSummary = await OilRepository.GetSummaryAsync(OilPriceApiClient.BrentCode); }
            catch (Exception) { OilSummary = null; }
        }

        // 舊資料庫沒有這張表時只是不顯示摘要，不影響其他功能
        private async Task LoadSoxSummaryAsync()
        {
            try { SoxSummary = await MarketIndexRepository.GetSummaryAsync(MarketIndexApiClient.SoxCode); }
            catch (Exception) { SoxSummary = null; }
        }

        private async Task LoadYahooOilSummaryAsync()
        {
            try { YahooOilSummary = await OilRepository.GetSummaryAsync(YahooOilSyncService.Code); }
            catch (Exception) { YahooOilSummary = null; }
        }

        /// <summary>下載 Yahoo Brent 期貨 (BZ=F) 全部日線 (一次請求)</summary>
        public async Task SyncYahooOil()
        {
            if (IsSyncingYahooOil) return;
            IsSyncingYahooOil = true;
            YahooOilStatusMessage = "下載中...";
            YahooOilIsError = false;
            try
            {
                var result = await YahooOilSync.SyncAsync(CancellationToken.None);
                await LoadYahooOilSummaryAsync();
                if (result.Error != null)
                {
                    YahooOilStatusMessage = $"Yahoo 原油同步失敗：{result.Error}";
                    YahooOilIsError = true;
                }
                else
                {
                    YahooOilStatusMessage = $"Yahoo 原油同步完成：寫入 {result.RowsWritten} 筆（{result.FirstDate} ~ {result.LastDate}）。";
                }
            }
            finally
            {
                IsSyncingYahooOil = false;
            }
        }

        /// <summary>下載最近一年的費半日線 (一次請求)</summary>
        public async Task SyncSox()
        {
            if (IsSyncingSox) return;
            IsSyncingSox = true;
            SoxStatusMessage = "下載中...";
            SoxIsError = false;
            try
            {
                var result = await MarketIndexSync.SyncSoxAsync(CancellationToken.None);
                await LoadSoxSummaryAsync();
                if (result.Error != null)
                {
                    SoxStatusMessage = $"費半同步失敗：{result.Error}";
                    SoxIsError = true;
                }
                else
                {
                    SoxStatusMessage = $"費半同步完成：寫入 {result.RowsWritten} 筆（{result.FirstDate} ~ {result.LastDate}）。";
                }
            }
            finally
            {
                IsSyncingSox = false;
            }
        }

        public void OpenSettingsFile() => RunOpen(PathOpener.OpenSettingsFile, "已用文字編輯器開啟 appsettings.json，改好存檔後需重新啟動網站才會生效。");

        public void OpenSettingsFolder() => RunOpen(PathOpener.OpenSettingsFolder, "已開啟 appsettings.json 所在資料夾。");

        private void RunOpen(Action open, string successMessage)
        {
            try
            {
                open();
                OilStatusMessage = successMessage;
                OilIsError = false;
            }
            catch (Exception ex)
            {
                OilStatusMessage = ex.Message;
                OilIsError = true;
            }
        }

        /// <summary>下載最近一年的 Brent 每日價格 (一次請求)</summary>
        public async Task SyncOil()
        {
            if (IsSyncingOil) return;
            IsSyncingOil = true;
            OilStatusMessage = "下載中...";
            OilIsError = false;
            try
            {
                var result = await OilSync.SyncAsync(CancellationToken.None);
                await LoadOilSummaryAsync();
                if (result.Error != null)
                {
                    OilStatusMessage = $"原油同步失敗：{result.Error}";
                    OilIsError = true;
                }
                else
                {
                    OilStatusMessage = $"原油同步完成：寫入 {result.RowsWritten} 筆（{result.FirstDate} ~ {result.LastDate}）。";
                }
            }
            finally
            {
                IsSyncingOil = false;
            }
        }

        /// <summary>同步範圍的起日 (含)</summary>
        public static DateTime InstitutionalFromDate(string range, DateTime today) => range switch
        {
            "1m" => today.AddMonths(-1).AddDays(1),
            _ => today.AddDays(-6),
        };

        /// <summary>
        /// 逐日下載三大法人買賣超與外資持股 (由新到舊)，每天完成即寫入資料庫
        /// </summary>
        public async Task SyncInstitutional()
        {
            if (IsSyncingInstitutional) return;

            IsSyncingInstitutional = true;
            _institutionalCts = new CancellationTokenSource();
            var started = DateTime.Now;
            SetInstitutionalStatus("準備同步三大法人資料...", false);

            // 不用 Progress<T>：它的回呼是非同步排入的，可能在同步結束後才執行，蓋掉最後的結果訊息
            var progress = new SyncProgress<InstitutionalSyncProgress>(p =>
            {
                string eta = "";
                if (p.Done > 0 && p.Done < p.Total)
                {
                    // 已略過的日期很快，估算只是參考
                    double secondsPerDay = (DateTime.Now - started).TotalSeconds / p.Done;
                    eta = $"，預估剩餘約 {Math.Ceiling(secondsPerDay * (p.Total - p.Done) / 60)} 分鐘";
                }
                SetInstitutionalStatus($"[{p.Done}/{p.Total}] {p.Message}{eta}", false);
            });

            try
            {
                var today = DateTime.Today;
                var result = await InstitutionalSync.SyncAsync(InstitutionalFromDate(InstitutionalRange, today), today, progress, _institutionalCts.Token);
                await LoadInstitutionalSummaryAsync();

                string summary = $"新增 {result.DaysWithData} 個交易日、寫入 {result.RowsWritten} 筆，向證交所請求 {result.Requests} 次（略過已下載 {result.SkippedChecks} 項）。";
                if (result.Error != null)
                {
                    SetInstitutionalStatus($"同步中斷：{result.Error}。已完成的日期都已存入，稍後再按一次同步即可從中斷處繼續。{summary}", true);
                }
                else if (result.Cancelled)
                {
                    SetInstitutionalStatus($"已停止。{summary}", false);
                }
                else
                {
                    SetInstitutionalStatus($"三大法人同步完成。{summary}", false);
                }
            }
            catch (Exception ex)
            {
                SetInstitutionalStatus($"三大法人同步失敗: {ex.Message}", true);
            }
            finally
            {
                IsSyncingInstitutional = false;
                _institutionalCts.Dispose();
                _institutionalCts = null;
                await InvokeAsync(StateHasChanged);
            }
        }

        private sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
        {
            public void Report(T value) => onReport(value);
        }

        public async Task StopInstitutionalSync()
        {
            if (!await ConfirmStopAsync("三大法人同步", "已完成的日期不會重抓，再按一次同步即可接著跑")) return;
            _institutionalCts?.Cancel();
            SetInstitutionalStatus("正在停止...", false);
        }

        /// <summary>
        /// 用 FinMind 逐檔下載多年三大法人買賣超與外資持股；IP 被封鎖或達上限時立即停止
        /// </summary>
        // 依產業別：把該產業全部股票代號帶入輸入框，再照原本的流程同步
        public async Task SyncFinMindByIndustry()
        {
            if (IsSyncingFinMind) return;
            var ids = await LoadIndustryIdsAsync(FinMindIndustry, m => SetFinMindStatus(m, true));
            if (ids == null) return;
            FinMindStockIds = string.Join(",", ids);
            await SyncFinMind();
        }

        public async Task SyncFinMind()
        {
            if (IsSyncingFinMind) return;

            var stockIds = FinMindStockIds
                .Split(new[] { ',', '，', '、', ' ', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();
            if (stockIds.Count == 0)
            {
                SetFinMindStatus("請輸入至少一檔股票代號！", true);
                return;
            }

            IsSyncingFinMind = true;
            _finMindCts = new CancellationTokenSource();
            SetFinMindStatus($"準備同步 {stockIds.Count} 檔...", false);
            var progress = new SyncProgress<FinMindSyncProgress>(p => SetFinMindStatus($"[{p.Done}/{p.Total}] {p.Message}", false));

            try
            {
                var result = await FinMindSync.SyncAsync(stockIds, FinMindFullReload, progress, _finMindCts.Token);
                await LoadInstitutionalSummaryAsync();

                string summary = $"完成 {result.StocksDone}/{stockIds.Count} 檔、寫入 {result.RowsWritten} 筆，向 FinMind 請求 {result.Requests} 次。";
                if (result.Error != null)
                {
                    string retry = result.Blocked ? "" : "已完成的股票都已存入，稍後再按一次同步即可。";
                    SetFinMindStatus($"同步中斷：{result.Error}。{retry}{summary}", true);
                }
                else if (result.Cancelled)
                {
                    SetFinMindStatus($"已停止。{summary}", false);
                }
                else
                {
                    SetFinMindStatus($"FinMind 同步完成。{summary}", false);
                }
            }
            catch (Exception ex)
            {
                SetFinMindStatus($"FinMind 同步失敗: {ex.Message}", true);
            }
            finally
            {
                IsSyncingFinMind = false;
                _finMindCts.Dispose();
                _finMindCts = null;
                await InvokeAsync(StateHasChanged);
            }
        }

        public async Task StopFinMindSync()
        {
            if (!await ConfirmStopAsync("FinMind 三大法人同步", "再按一次同步即可接著跑")) return;
            _finMindCts?.Cancel();
            SetFinMindStatus("正在停止...", false);
        }

        private void SetFinMindStatus(string msg, bool isError)
        {
            FinMindStatusMessage = msg;
            FinMindIsError = isError;
            InvokeAsync(StateHasChanged);
        }

        private void SetInstitutionalStatus(string msg, bool isError)
        {
            InstitutionalStatusMessage = msg;
            InstitutionalIsError = isError;
            InvokeAsync(StateHasChanged);
        }

        private async Task LoadNameSummaryAsync()
        {
            try
            {
                NameSummary = await StockNameRepository.GetSummaryAsync();
            }
            catch (Exception ex)
            {
                ListStatusMessage = $"讀取股票清單失敗: {ex.Message}";
                ListIsError = true;
            }
        }

        /// <summary>
        /// 從證交所下載上市股票清單 (含 ETF) 並寫入 StockNameList 資料表 (不會修改 StockList)
        /// </summary>
        public async Task SyncStockList()
        {
            if (IsSyncingList) return;

            IsSyncingList = true;
            ListIsError = false;
            ListStatusMessage = "正在從證交所下載股票清單...";

            try
            {
                var stocks = await TwseApi.GetListedStocksAsync();
                if (stocks.Count == 0)
                {
                    ListStatusMessage = "證交所沒有回傳任何股票資料，請稍後再試。";
                    ListIsError = true;
                    return;
                }

                await StockNameRepository.UpsertStocksAsync(stocks);
                await LoadNameSummaryAsync();

                // 讓股票代號輸入框的自動完成選單立即包含新清單
                if (_stockIdDataList != null)
                {
                    await _stockIdDataList.ReloadAsync();
                }

                ListStatusMessage = $"股票清單更新完成，共 {stocks.Count} 檔。";
            }
            catch (Exception ex)
            {
                ListStatusMessage = $"股票清單更新失敗: {ex.Message}";
                ListIsError = true;
            }
            finally
            {
                IsSyncingList = false;
            }
        }

        /// <summary>
        /// 由近至遠逐月向證交所下載資料；連續 MaxConsecutiveEmpty 次無資料自動中斷，最後一次寫入 SQLite。
        /// </summary>
        public async Task SyncFromApi()
        {
            if (string.IsNullOrWhiteSpace(BatchStockId))
            {
                SetStatus("請指定要從 API 同步的股票代號！", true);
                return;
            }

            string stockId = BatchStockId.Trim();

            if (!int.TryParse(SelectedRange, out int totalMonthsToSync))
            {
                totalMonthsToSync = 1;
            }

            IsProcessing = true;
            SetStatus($"開始同步代號 {stockId} 的歷史數據 (設定範圍: {totalMonthsToSync} 個月)...", false);

            _priceCts = new CancellationTokenSource();
            try
            {
                var progress = new Progress<PriceSyncProgress>(p =>
                    SetStatus($"[{p.Index}/{p.Total}] 正在下載 {p.Month:yyyy年MM月} 的資料... (連續無資料: {p.ConsecutiveEmpty}/{p.MaxConsecutiveEmpty})", false));
                var result = await PriceSync.SyncAsync(stockId, totalMonthsToSync, progress, _priceCts.Token);

                if (result.RowsFetched == 0)
                {
                    string alertMsg;
                    if (result.LastError != null)
                    {
                        alertMsg = $"同步失敗，無法從證交所取得資料。最後一次錯誤 → {result.LastError}";
                    }
                    else if (totalMonthsToSync == 1)
                    {
                        alertMsg = $"證交所目前沒有 {stockId} 在 {result.ThisMonth:yyyy年MM月} 的交易資料（月初尚無交易日時屬正常），請改選「近三個月」以上的範圍。";
                    }
                    else
                    {
                        alertMsg = $"證交所沒有 {stockId} 在這段期間的交易資料。請確認代號正確，且為上市股票 / ETF（上櫃股票目前查不到）。";
                    }
                    SetStatus(alertMsg, true);
                    return;
                }

                string finalStatus = $"全部更新程序結束。成功撈取 {result.SuccessMonths} 個月份，共更新 {result.RowsWritten} 筆 K 線資料至 SQLite。";
                if (result.Broken)
                {
                    finalStatus += $" (註: 因連續 {PriceSyncService.MaxConsecutiveEmpty} 次無資料，已提前終止歷史追溯)";
                }
                if (result.LastError != null)
                {
                    finalStatus += $" (另有 {result.ErrorCount} 個月份下載失敗，最後一次錯誤 → {result.LastError})";
                }

                SetStatus(finalStatus, false);
            }
            catch (Exception ex)
            {
                SetStatus($"大批次同步失敗: {ex.Message}", true);
            }
            finally
            {
                IsProcessing = false;
                _priceCts.Dispose();
                _priceCts = null;
            }
        }

        private void SetStatus(string msg, bool isError)
        {
            StatusMessage = msg;
            IsError = isError;

            // 強制 Blazor 重新渲染 UI 畫面：
            // 這樣在 for 迴圈每跑一次時，前端才能即時看到「正在下載 2026年05月...」的文字變化
            InvokeAsync(StateHasChanged);
        }
    }
}
