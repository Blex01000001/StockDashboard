using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Services
{
    public class MarketIndexSyncResult
    {
        public int RowsWritten { get; set; }
        public string? FirstDate { get; set; }   // 這次取回資料的日期範圍
        public string? LastDate { get; set; }
        public bool Cancelled { get; set; }
        public string? Error { get; set; }
    }

    public interface IMarketIndexSyncService
    {
        Task<MarketIndexSyncResult> SyncSoxAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 費半 (SOX) 同步：一次請求取回最近一年的日線，寫入 MarketIndexDaily (同日覆蓋)。
    /// 只抓一年 (使用者指定)，更早的資料靠每次同步慢慢累積 (之前存的不會刪)。
    /// </summary>
    public class MarketIndexSyncService : IMarketIndexSyncService
    {
        public const int HistoryDays = 365;

        private readonly IMarketIndexApiClient _api;
        private readonly IMarketIndexRepository _repository;
        private readonly Func<DateTime> _utcNow;

        public MarketIndexSyncService(IMarketIndexApiClient api, IMarketIndexRepository repository)
            : this(api, repository, () => DateTime.UtcNow) { }

        // 測試用：可指定「現在時間」
        public MarketIndexSyncService(IMarketIndexApiClient api, IMarketIndexRepository repository, Func<DateTime> utcNow)
        {
            _api = api;
            _repository = repository;
            _utcNow = utcNow;
        }

        public async Task<MarketIndexSyncResult> SyncSoxAsync(CancellationToken cancellationToken)
        {
            var result = new MarketIndexSyncResult();
            try
            {
                var to = _utcNow().Date;
                var rows = await _api.GetDailyAsync(MarketIndexApiClient.SoxCode, MarketIndexApiClient.SoxSymbol, to.AddDays(-HistoryDays), to, cancellationToken);
                if (rows.Count == 0)
                {
                    result.Error = "Yahoo 沒有回傳費半日線資料";
                    return result;
                }
                result.FirstDate = rows[0].TradeDate;
                result.LastDate = rows[^1].TradeDate;
                result.RowsWritten = await _repository.UpsertAsync(rows);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result.Cancelled = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return result;
        }
    }
}
