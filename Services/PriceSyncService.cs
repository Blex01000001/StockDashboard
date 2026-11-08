using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Services
{
    /// <summary>日線同步進度 (資料更新頁顯示用)：Index 從 1 開始</summary>
    public record PriceSyncProgress(int Index, int Total, DateTime Month, int ConsecutiveEmpty, int MaxConsecutiveEmpty);

    /// <summary>日線同步結果</summary>
    public class PriceSyncResult
    {
        public int MonthsTotal { get; set; }        // 設定要同步的月數
        public int SuccessMonths { get; set; }      // 有抓到資料的月份數
        public int ErrorCount { get; set; }         // 下載失敗的月份數
        public string? LastError { get; set; }      // 最後一次下載失敗的原因
        public bool Broken { get; set; }            // 因連續無資料而提前中斷
        public bool Cancelled { get; set; }
        public int RowsFetched { get; set; }        // 抓到的資料筆數 (寫入前)
        public int RowsWritten { get; set; }        // 實際寫入 SQLite 的筆數
        public DateTime ThisMonth { get; set; }     // 本月 (第一天)，空結果訊息用
    }

    public interface IPriceSyncService
    {
        Task<PriceSyncResult> SyncAsync(string stockId, int months, IProgress<PriceSyncProgress>? progress, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 個股日線同步：由本月起逐月往前向證交所下載，最後一次寫入 SQLite。
    /// - 連續 MaxConsecutiveEmpty 個月無資料 (本月不計) 或失敗就提前中斷
    /// - 每次請求間隔 Twse:RequestDelayMs (預設 2500 毫秒)，避免被證交所封鎖
    /// - 取消時不再請求，已抓到的資料仍會寫入
    /// </summary>
    public class PriceSyncService : IPriceSyncService
    {
        // 連續幾個月沒有資料就停止往前追溯 (通常代表已超過上市日期，或被證交所限流)
        public const int MaxConsecutiveEmpty = 3;

        private readonly ITwseApiClient _twse;
        private readonly IStockPriceRepository _repository;
        private readonly int _delayMs;
        private readonly Func<DateTime> _today;

        public PriceSyncService(ITwseApiClient twse, IStockPriceRepository repository, IConfiguration configuration)
            : this(twse, repository, int.TryParse(configuration["Twse:RequestDelayMs"], out int ms) ? ms : 2500, () => DateTime.Today) { }

        // 測試用：可指定請求間隔與「今天」
        public PriceSyncService(ITwseApiClient twse, IStockPriceRepository repository, int delayMs, Func<DateTime> today)
        {
            _twse = twse;
            _repository = repository;
            _delayMs = delayMs;
            _today = today;
        }

        public async Task<PriceSyncResult> SyncAsync(string stockId, int months, IProgress<PriceSyncProgress>? progress, CancellationToken cancellationToken)
        {
            var today = _today();
            var thisMonth = new DateTime(today.Year, today.Month, 1);
            var result = new PriceSyncResult { MonthsTotal = months, ThisMonth = thisMonth };
            var all = new List<StockDailyPriceEntity>();
            int consecutiveEmpty = 0;

            // 由本月開始往回推 (i = 0 是本月，i = 1 是上個月 ...)
            for (int i = 0; i < months; i++)
            {
                if (cancellationToken.IsCancellationRequested) { result.Cancelled = true; break; }

                var monthDate = thisMonth.AddMonths(-i);
                progress?.Report(new PriceSyncProgress(i + 1, months, monthDate, consecutiveEmpty, MaxConsecutiveEmpty));

                try
                {
                    var monthData = await _twse.GetMonthlyPricesAsync(stockId, monthDate, cancellationToken);
                    if (monthData.Count > 0)
                    {
                        all.AddRange(monthData);
                        result.SuccessMonths++;
                        consecutiveEmpty = 0; // 只要成功一次，連續無資料計數器立刻歸零
                    }
                    else if (i > 0)
                    {
                        // 本月 (i = 0) 沒資料很正常 (例如月初還沒有交易日)，不計入熔斷
                        consecutiveEmpty++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    result.LastError = $"{monthDate:yyyy年MM月}：{ex.Message}";
                    consecutiveEmpty++; // 網路異常或 API 報錯也計入失敗
                }

                // 熔斷機制：連續 N 次沒有資料就中斷
                if (consecutiveEmpty >= MaxConsecutiveEmpty)
                {
                    result.Broken = true;
                    break;
                }

                // 防封鎖機制：如果還需要查下一個月，強迫延遲
                if (i < months - 1 && _delayMs > 0)
                {
                    try { await Task.Delay(_delayMs, cancellationToken); }
                    catch (OperationCanceledException) { result.Cancelled = true; break; }
                }
            }

            result.RowsFetched = all.Count;
            if (all.Count > 0) result.RowsWritten = await _repository.UpsertPricesAsync(all);
            return result;
        }
    }
}
