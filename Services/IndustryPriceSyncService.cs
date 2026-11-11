using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Services
{
    /// <summary>依產業批次同步的進度 (資料更新頁顯示用)</summary>
    public record IndustryPriceSyncProgress(string IndustryCode, int StockIndex, int StockTotal, string StockId, int Requests, int RowsWritten, string Message);

    public class IndustryPriceSyncResult
    {
        public int StocksTotal { get; set; }       // 該產業的股票檔數
        public int StocksDone { get; set; }        // 已處理完 (追溯到上市日或達月數上限) 的檔數
        public int Requests { get; set; }          // 向證交所發出的請求數
        public int RowsWritten { get; set; }       // 寫入的日線筆數
        public bool Cancelled { get; set; }
        public string? Error { get; set; }         // 中途失敗的原因 (多半是被證交所暫時封鎖)
    }

    public interface IIndustryPriceSyncService
    {
        // maxMonths：每檔最多往前幾個月 (預設到上市日；例如 3 = 只更新近三個月)
        Task<IndustryPriceSyncResult> SyncAsync(string industryCode, IProgress<IndustryPriceSyncProgress>? progress, CancellationToken cancellationToken, int maxMonths = IndustryPriceSyncService.MaxMonths);
    }

    /// <summary>
    /// 依產業別批次同步日線：該產業每一檔股票，由本月往前逐月下載，直到上市日 (有多少撈多少)。
    /// - 可續跑：已完整的歷史月份不重抓 (資料庫該月最後交易日 ≥ 25 號且早於本月)；已追溯到上市日的股票不再往前查
    /// - 每檔抓完立刻寫入；中途停止、被封鎖都不會白抓，下次再按一次會接著跑
    /// - 遇到任何下載錯誤 (多半是被封鎖) 立即停止整批，不當成「沒資料」，避免把每一檔都誤判成已追溯到上市日
    /// - 請求間隔 Twse:BatchRequestDelayMs (預設 3000 毫秒，比單檔同步保守)
    /// </summary>
    public class IndustryPriceSyncService : IIndustryPriceSyncService
    {
        public const int MaxMonths = 480;            // 往前最多 40 年 (證交所日線最早約 1992 年)
        public const int MaxConsecutiveEmpty = PriceSyncService.MaxConsecutiveEmpty;
        public const int CompleteDay = 25;           // 月份的最後交易日 ≥ 這個日期才算抓完整

        private readonly ITwseApiClient _twse;
        private readonly IStockPriceRepository _prices;
        private readonly IPriceCoverageRepository _coverage;
        private readonly IIndustryRepository _industry;
        private readonly int _delayMs;
        private readonly Func<DateTime> _today;

        public IndustryPriceSyncService(ITwseApiClient twse, IStockPriceRepository prices, IPriceCoverageRepository coverage, IIndustryRepository industry, IConfiguration configuration)
            : this(twse, prices, coverage, industry, int.TryParse(configuration["Twse:BatchRequestDelayMs"], out int ms) ? ms : 3000, () => DateTime.Today) { }

        // 測試用：可指定請求間隔與「今天」
        public IndustryPriceSyncService(ITwseApiClient twse, IStockPriceRepository prices, IPriceCoverageRepository coverage, IIndustryRepository industry, int delayMs, Func<DateTime> today)
        {
            _twse = twse;
            _prices = prices;
            _coverage = coverage;
            _industry = industry;
            _delayMs = delayMs;
            _today = today;
        }

        /// <summary>這個月份是否已完整在資料庫 (不必再抓)：早於本月，且已有資料、最後交易日在 25 號之後</summary>
        public static bool IsMonthComplete(DateTime month, DateTime thisMonth, IReadOnlyDictionary<string, string> lastDates)
        {
            if (month >= thisMonth) return false;
            if (!lastDates.TryGetValue(month.ToString("yyyy-MM"), out var last)) return false;
            return last.Length >= 10 && int.TryParse(last.AsSpan(8, 2), out int day) && day >= CompleteDay;
        }

        public async Task<IndustryPriceSyncResult> SyncAsync(string industryCode, IProgress<IndustryPriceSyncProgress>? progress, CancellationToken cancellationToken, int maxMonths = MaxMonths)
        {
            maxMonths = Math.Clamp(maxMonths, 1, MaxMonths);
            var result = new IndustryPriceSyncResult();
            string code = industryCode.Trim();
            var stockIds = await _industry.GetStockIdsByIndustryAsync(code);
            result.StocksTotal = stockIds.Count;
            if (stockIds.Count == 0)
            {
                result.Error = $"StockList 找不到產業別 {code} 的股票（請確認代碼，且已有「產業別」欄位）";
                return result;
            }

            var today = _today();
            var thisMonth = new DateTime(today.Year, today.Month, 1);

            for (int s = 0; s < stockIds.Count; s++)
            {
                string stockId = stockIds[s];
                void Report(string message) => progress?.Report(new IndustryPriceSyncProgress(code, s + 1, stockIds.Count, stockId, result.Requests, result.RowsWritten, message));

                if (cancellationToken.IsCancellationRequested) { result.Cancelled = true; return result; }
                Report("檢查已有的資料...");

                var lastDates = await _coverage.GetMonthLastDatesAsync(stockId);
                bool reached = await _coverage.IsHistoryReachedAsync(stockId);
                string? earliest = lastDates.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();

                var fetched = new List<StockDailyPriceEntity>();
                int consecutiveEmpty = 0;
                bool finishedHistory = false;
                bool stop = false;

                for (int i = 0; i < maxMonths; i++)
                {
                    var month = thisMonth.AddMonths(-i);
                    string key = month.ToString("yyyy-MM");

                    if (IsMonthComplete(month, thisMonth, lastDates)) { consecutiveEmpty = 0; continue; }

                    // 已追溯到上市日的股票：比資料庫最早的月份更早的月份不必再查
                    if (reached && earliest != null && string.CompareOrdinal(key, earliest) < 0) { finishedHistory = true; break; }

                    if (cancellationToken.IsCancellationRequested) { result.Cancelled = true; stop = true; break; }

                    // 每次請求前等待 (整批第一次除外)，避免被證交所封鎖
                    if (result.Requests > 0 && _delayMs > 0)
                    {
                        try { await Task.Delay(_delayMs, cancellationToken); }
                        catch (OperationCanceledException) { result.Cancelled = true; stop = true; break; }
                    }

                    Report($"下載 {month:yyyy年MM月}（連續無資料 {consecutiveEmpty}/{MaxConsecutiveEmpty}）");
                    try
                    {
                        result.Requests++;
                        var monthData = await _twse.GetMonthlyPricesAsync(stockId, month, cancellationToken);
                        if (monthData.Count > 0)
                        {
                            fetched.AddRange(monthData);
                            consecutiveEmpty = 0;
                        }
                        else if (i > 0)
                        {
                            consecutiveEmpty++;   // 本月 (i = 0) 沒資料很正常，不計入
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        result.Cancelled = true; stop = true; break;
                    }
                    catch (Exception ex)
                    {
                        // 多半是被封鎖或網路問題：不當成沒資料，立即停止整批；已抓到的仍會寫入
                        result.Error = $"{stockId} {month:yyyy年MM月}：{ex.Message}";
                        stop = true;
                        break;
                    }

                    if (consecutiveEmpty >= MaxConsecutiveEmpty) { finishedHistory = true; break; }
                }

                if (fetched.Count > 0) result.RowsWritten += await _prices.UpsertPricesAsync(fetched);
                if (stop) return result;

                // 連續無資料 (或之前已追溯到上市日) → 記下，之後不再往前查
                if (finishedHistory && !reached) await _coverage.SetHistoryReachedAsync(stockId);
                result.StocksDone++;
                Report($"完成（累計寫入 {result.RowsWritten} 筆）");
            }

            return result;
        }
    }
}
