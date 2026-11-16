using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StockDashboard.Components.Shared;
using StockDashboard.Models;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;
using StockDashboard.Services;

namespace StockDashboard.Components.UpdateSources
{
    /// <summary>FinMind 來源的同步卡片：個股多年三大法人、除權除息 (還原股價用)</summary>
    public partial class FinMindSyncSection : ComponentBase, IDisposable
    {
        [Inject] private IInstitutionalRepository InstitutionalRepository { get; set; } = default!;
        [Inject] private IFinMindSyncService FinMindSync { get; set; } = default!;
        [Inject] private IStockCorporateActionSyncService ActionSync { get; set; } = default!;
        [Inject] private IStockCorporateActionRepository ActionRepository { get; set; } = default!;
        [Inject] private IIndustryRepository IndustryRepository { get; set; } = default!;
        [Inject] private IJSRuntime JS { get; set; } = default!;

        public List<IndustryOption> Industries { get; private set; } = new();   // 讀取失敗或沒有產業別欄位時為空

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

        // ── FinMind 除權除息 (還原股價用) ──
        public string ActionStockIds { get; set; } = string.Empty;
        public string ActionIndustry { get; set; } = string.Empty;
        public (int Stocks, int Events)? ActionSummary { get; set; }
        public bool IsSyncingAction { get; set; } = false;
        public string ActionStatusMessage { get; set; } = string.Empty;
        public bool ActionIsError { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            Industries = await SyncUi.LoadIndustriesAsync(IndustryRepository);
            await LoadFinMindSummaryAsync();
            await LoadActionSummaryAsync();
        }

        public void Dispose()
        {
            // 離開頁面時停止同步 (同步中的頁面元件已銷毀，進度無處顯示)
            _finMindCts?.Cancel();
            _actionCts?.Cancel();
        }

        private Task<bool> ConfirmStopAsync(string what, string note) => SyncUi.ConfirmStopAsync(JS, what, note);

        private Task<List<string>?> LoadIndustryIdsAsync(string industryCode, Action<string> report) =>
            SyncUi.LoadIndustryIdsAsync(IndustryRepository, industryCode, report);

        private async Task LoadFinMindSummaryAsync()
        {
            try
            {
                FinMindSyncedCount = (await InstitutionalRepository.GetFinMindSyncedAsync(FinMindSyncService.TradingDataset)).Count;
            }
            catch (Exception ex)
            {
                SetFinMindStatus($"讀取三大法人資料失敗: {ex.Message}", true);
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

        public async Task SyncActions()
        {
            if (IsSyncingAction) return;

            var stockIds = StockIdParser.Parse(ActionStockIds);
            if (stockIds.Count == 0)
            {
                SetActionStatus("請輸入至少一檔股票代號！", true);
                return;
            }

            IsSyncingAction = true;
            _actionCts = new CancellationTokenSource();
            SetActionStatus($"準備同步 {stockIds.Count} 檔...", false);
            var progress = new SyncUi.Progress<StockCorporateActionSyncProgress>(p => SetActionStatus($"[{p.Done}/{p.Total}] {p.Message}", false));

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

            var stockIds = StockIdParser.Parse(FinMindStockIds);
            if (stockIds.Count == 0)
            {
                SetFinMindStatus("請輸入至少一檔股票代號！", true);
                return;
            }

            IsSyncingFinMind = true;
            _finMindCts = new CancellationTokenSource();
            SetFinMindStatus($"準備同步 {stockIds.Count} 檔...", false);
            var progress = new SyncUi.Progress<FinMindSyncProgress>(p => SetFinMindStatus($"[{p.Done}/{p.Total}] {p.Message}", false));

            try
            {
                var result = await FinMindSync.SyncAsync(stockIds, FinMindFullReload, progress, _finMindCts.Token);
                await LoadFinMindSummaryAsync();

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
    }
}
