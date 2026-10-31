using Microsoft.AspNetCore.Components;
using StockDashboard.Components.Shared;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;
using StockDashboard.Services;

namespace StockDashboard.Components.Pages
{
    public partial class StockUpdate : ComponentBase
    {
        // 連續幾個月沒有資料就停止往前追溯 (通常代表已超過上市日期，或被證交所限流)
        private const int MaxConsecutiveEmpty = 3;

        // 每次請求證交所之間的間隔：證交所對短時間大量請求會暫時封鎖 IP
        private const int RequestDelayMs = 2500;

        [Inject] private ITwseApiClient TwseApi { get; set; } = default!;
        [Inject] private IStockPriceRepository PriceRepository { get; set; } = default!;
        [Inject] private IStockListRepository StockListRepository { get; set; } = default!;

        public StockDailyPriceEntity InputPrice { get; set; } = new();
        public string BatchStockId { get; set; } = "2330";
        public string StatusMessage { get; set; } = string.Empty;
        public bool IsError { get; set; } = false;
        public bool IsProcessing { get; set; } = false;
        public string SelectedRange { get; set; } = "1";

        // ── 股票清單同步 ──
        public int StockListCount { get; set; }
        public string? StockListUpdatedAt { get; set; }
        public bool IsSyncingList { get; set; } = false;
        public string ListStatusMessage { get; set; } = string.Empty;
        public bool ListIsError { get; set; } = false;
        private StockIdDataList? _stockIdDataList;

        protected override async Task OnInitializedAsync()
        {
            InputPrice.TradeDate = DateTime.Today.ToString("yyyy-MM-dd");
            await LoadStockListSummaryAsync();
        }

        private async Task LoadStockListSummaryAsync()
        {
            try
            {
                (StockListCount, StockListUpdatedAt) = await StockListRepository.GetSummaryAsync();
            }
            catch (Exception ex)
            {
                ListStatusMessage = $"讀取股票清單失敗: {ex.Message}";
                ListIsError = true;
            }
        }

        /// <summary>
        /// 從證交所下載上市股票清單並寫入 StockList 資料表
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

                await StockListRepository.UpsertStocksAsync(stocks);
                await LoadStockListSummaryAsync();

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
        /// 儲存單筆手動輸入的股價資料
        /// </summary>
        public async Task SaveSinglePrice()
        {
            if (string.IsNullOrWhiteSpace(InputPrice.StockId) || string.IsNullOrWhiteSpace(InputPrice.TradeDate))
            {
                SetStatus("股票代號與交易日期不可為空！", true);
                return;
            }

            IsProcessing = true;
            SetStatus("正在寫入資料庫...", false);

            try
            {
                await PriceRepository.UpsertPricesAsync(new[] { InputPrice });
                SetStatus($"成功！股票 {InputPrice.StockId} 在 {InputPrice.TradeDate} 的資料已成功同步更新。", false);
            }
            catch (Exception ex)
            {
                SetStatus($"寫入資料庫失敗: {ex.Message}", true);
            }
            finally
            {
                IsProcessing = false;
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
                        else
                        {
                            consecutiveEmptyCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"{monthDate:yyyyMM} 下載失敗: {ex.Message}");
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
                    string alertMsg = isBreaked
                        ? $"同步中斷！由於連續 {consecutiveEmptyCount} 次未能從 API 取得資料（可能已達該股上市極限或遭頻率限制）。"
                        : "資料同步失敗，未撈取到任何有效數據。";
                    SetStatus(alertMsg, true);
                    return;
                }

                int affectedRows = await PriceRepository.UpsertPricesAsync(allCleanedData);

                string finalStatus = $"全部更新程序結束。成功撈取 {successCount} 個月份，共更新 {affectedRows} 筆 K 線資料至 SQLite。";
                if (isBreaked)
                {
                    finalStatus += $" (註: 因連續 {MaxConsecutiveEmpty} 次無資料，已提前終止歷史追溯)";
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
