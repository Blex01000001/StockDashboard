using Microsoft.AspNetCore.Components;
using StockDashboard.Components.Shared;
using StockDashboard.Models;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;
using StockDashboard.Services;

namespace StockDashboard.Components.Pages
{
    public partial class StockUpdate : ComponentBase, IDisposable
    {
        // 連續幾個月沒有資料就停止往前追溯 (通常代表已超過上市日期，或被證交所限流)
        private const int MaxConsecutiveEmpty = 3;

        // 每次請求證交所之間的間隔 (Twse:RequestDelayMs，預設 2500 毫秒)：證交所對短時間大量請求會暫時封鎖 IP
        private int RequestDelayMs => int.TryParse(Configuration["Twse:RequestDelayMs"], out int ms) ? ms : 2500;

        [Inject] private IConfiguration Configuration { get; set; } = default!;
        [Inject] private ITwseApiClient TwseApi { get; set; } = default!;
        [Inject] private IStockPriceRepository PriceRepository { get; set; } = default!;
        [Inject] private IStockNameRepository StockNameRepository { get; set; } = default!;
        [Inject] private IInstitutionalRepository InstitutionalRepository { get; set; } = default!;
        [Inject] private IInstitutionalSyncService InstitutionalSync { get; set; } = default!;
        [Inject] private IFinMindSyncService FinMindSync { get; set; } = default!;
        [Inject] private IOilPriceSyncService OilSync { get; set; } = default!;
        [Inject] private IOilPriceRepository OilRepository { get; set; } = default!;
        [Inject] private ILocalPathOpener PathOpener { get; set; } = default!;
        [Inject] private IMarketIndexSyncService MarketIndexSync { get; set; } = default!;
        [Inject] private IMarketIndexRepository MarketIndexRepository { get; set; } = default!;

        public string BatchStockId { get; set; } = "2330";
        public string StatusMessage { get; set; } = string.Empty;
        public bool IsError { get; set; } = false;
        public bool IsProcessing { get; set; } = false;
        public string SelectedRange { get; set; } = "1";

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
        public bool FinMindFullReload { get; set; } = false;
        public int FinMindSyncedCount { get; set; }
        public bool IsSyncingFinMind { get; set; } = false;
        public string FinMindStatusMessage { get; set; } = string.Empty;
        public bool FinMindIsError { get; set; } = false;
        private CancellationTokenSource? _finMindCts;

        // ── 原油價格同步 ──
        public (string? FirstDate, string? LastDate, int Days)? OilSummary { get; set; }
        public bool IsSyncingOil { get; set; } = false;
        public string OilStatusMessage { get; set; } = string.Empty;
        public bool OilIsError { get; set; } = false;

        // ── 費半 (SOX) 同步 ──
        public (string? FirstDate, string? LastDate, int Days)? SoxSummary { get; set; }
        public bool IsSyncingSox { get; set; } = false;
        public string SoxStatusMessage { get; set; } = string.Empty;
        public bool SoxIsError { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadNameSummaryAsync();
            await LoadInstitutionalSummaryAsync();
            await LoadOilSummaryAsync();
            await LoadSoxSummaryAsync();
        }

        public void Dispose()
        {
            // 離開頁面時停止同步 (同步中的頁面元件已銷毀，進度無處顯示)
            _institutionalCts?.Cancel();
            _finMindCts?.Cancel();
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

        public void StopInstitutionalSync()
        {
            _institutionalCts?.Cancel();
            SetInstitutionalStatus("正在停止...", false);
        }

        /// <summary>
        /// 用 FinMind 逐檔下載多年三大法人買賣超與外資持股；IP 被封鎖或達上限時立即停止
        /// </summary>
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

        public void StopFinMindSync()
        {
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

            try
            {
                var allCleanedData = new List<StockDailyPriceEntity>();
                int successCount = 0;
                int consecutiveEmptyCount = 0;
                int errorCount = 0;
                string? lastError = null;   // 最後一次下載失敗的原因 (顯示在畫面上，方便判斷問題)
                bool isBreaked = false;

                // 由本月開始往回推 (i = 0 是本月，i = 1 是上個月 ...)
                DateTime thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

                for (int i = 0; i < totalMonthsToSync; i++)
                {
                    DateTime monthDate = thisMonth.AddMonths(-i);

                    SetStatus($"[{i + 1}/{totalMonthsToSync}] 正在下載 {monthDate:yyyy年MM月} 的資料... (連續無資料: {consecutiveEmptyCount}/{MaxConsecutiveEmpty})", false);

                    try
                    {
                        var monthData = await TwseApi.GetMonthlyPricesAsync(stockId, monthDate);

                        if (monthData.Count > 0)
                        {
                            allCleanedData.AddRange(monthData);
                            successCount++;
                            consecutiveEmptyCount = 0; // 只要成功一次，連續無資料計數器立刻歸零
                        }
                        else if (i > 0)
                        {
                            // 本月 (i = 0) 沒資料很正常 (例如月初還沒有交易日)，不計入熔斷
                            consecutiveEmptyCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        lastError = $"{monthDate:yyyy年MM月}：{ex.Message}";
                        consecutiveEmptyCount++; // 網路異常或 API 報錯也計入失敗
                    }

                    // 熔斷機制：連續 N 次沒有資料就中斷
                    if (consecutiveEmptyCount >= MaxConsecutiveEmpty)
                    {
                        isBreaked = true;
                        break;
                    }

                    // 防封鎖機制：如果還需要查下一個月，強迫延遲
                    if (i < totalMonthsToSync - 1)
                    {
                        await Task.Delay(RequestDelayMs);
                    }
                }

                if (allCleanedData.Count == 0)
                {
                    string alertMsg;
                    if (lastError != null)
                    {
                        alertMsg = $"同步失敗，無法從證交所取得資料。最後一次錯誤 → {lastError}";
                    }
                    else if (totalMonthsToSync == 1)
                    {
                        alertMsg = $"證交所目前沒有 {stockId} 在 {thisMonth:yyyy年MM月} 的交易資料（月初尚無交易日時屬正常），請改選「近三個月」以上的範圍。";
                    }
                    else
                    {
                        alertMsg = $"證交所沒有 {stockId} 在這段期間的交易資料。請確認代號正確，且為上市股票 / ETF（上櫃股票目前查不到）。";
                    }
                    SetStatus(alertMsg, true);
                    return;
                }

                int affectedRows = await PriceRepository.UpsertPricesAsync(allCleanedData);

                string finalStatus = $"全部更新程序結束。成功撈取 {successCount} 個月份，共更新 {affectedRows} 筆 K 線資料至 SQLite。";
                if (isBreaked)
                {
                    finalStatus += $" (註: 因連續 {MaxConsecutiveEmpty} 次無資料，已提前終止歷史追溯)";
                }
                if (lastError != null)
                {
                    finalStatus += $" (另有 {errorCount} 個月份下載失敗，最後一次錯誤 → {lastError})";
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
