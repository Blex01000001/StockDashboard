using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Services
{
    public class OilPriceSyncResult
    {
        public int RowsWritten { get; set; }
        public string? FirstDate { get; set; }   // 這次取回資料的日期範圍
        public string? LastDate { get; set; }
        public bool Cancelled { get; set; }
        public string? Error { get; set; }
    }

    public interface IOilPriceSyncService
    {
        Task<OilPriceSyncResult> SyncAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 原油同步：past_year 一次請求取回最近一年，寫入 OilPriceDaily (同日覆蓋)。
    /// 來源只提供一年，更早的資料靠每次同步慢慢累積 (之前存的不會刪)。
    /// </summary>
    public class OilPriceSyncService : IOilPriceSyncService
    {
        private readonly IOilPriceApiClient _api;
        private readonly IOilPriceRepository _repository;

        public OilPriceSyncService(IOilPriceApiClient api, IOilPriceRepository repository)
        {
            _api = api;
            _repository = repository;
        }

        public async Task<OilPriceSyncResult> SyncAsync(CancellationToken cancellationToken)
        {
            var result = new OilPriceSyncResult();
            try
            {
                var rows = await _api.GetPastYearAsync(cancellationToken);
                if (rows.Count == 0)
                {
                    result.Error = "oilpriceapi 沒有回傳每日價格資料";
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
