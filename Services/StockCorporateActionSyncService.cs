using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Services
{
    /// <summary>除權息資料來源。實際的 API Client (證交所或 FinMind) 實作此介面，回傳與來源無關的事件。</summary>
    public interface IStockCorporateActionApiClient
    {
        // 取得單一股票的除權息事件 (不限筆數，由呼叫端決定要寫入哪些)
        Task<List<StockCorporateActionEntity>> GetActionsAsync(string stockId, CancellationToken cancellationToken = default);
    }

    public class StockCorporateActionSyncResult
    {
        public int RowsWritten { get; set; }
        public string? FirstDate { get; set; }   // 這次取回事件的日期範圍
        public string? LastDate { get; set; }
        public bool Cancelled { get; set; }
        public string? Error { get; set; }
    }

    public interface IStockCorporateActionSyncService
    {
        Task<StockCorporateActionSyncResult> SyncAsync(string stockId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 除權息同步：取回單一股票的事件，寫入 StockCorporateAction (同日覆蓋，舊資料不刪)。
    /// 來源尚未決定，這裡不依賴任何特定 API。
    /// </summary>
    public class StockCorporateActionSyncService : IStockCorporateActionSyncService
    {
        private readonly IStockCorporateActionApiClient _api;
        private readonly IStockCorporateActionRepository _repository;

        public StockCorporateActionSyncService(IStockCorporateActionApiClient api, IStockCorporateActionRepository repository)
        {
            _api = api;
            _repository = repository;
        }

        public async Task<StockCorporateActionSyncResult> SyncAsync(string stockId, CancellationToken cancellationToken)
        {
            var result = new StockCorporateActionSyncResult();
            try
            {
                // 壞資料 (前收盤或參考價 <= 0、沒有日期) 不寫入；代號一律以要求的為準
                var rows = (await _api.GetActionsAsync(stockId, cancellationToken))
                    .Where(a => a.PreClose > 0 && a.RefPrice > 0 && !string.IsNullOrEmpty(a.ExDate))
                    .Select(a => new StockCorporateActionEntity { StockId = stockId, ExDate = a.ExDate, PreClose = a.PreClose, RefPrice = a.RefPrice })
                    .OrderBy(a => a.ExDate, StringComparer.Ordinal)
                    .ToList();
                if (rows.Count == 0)
                {
                    // 沒有事件不一定是錯 (很多股票從未除權息)，仍以訊息告知，不寫入
                    result.Error = $"{stockId} 沒有取得除權息資料";
                    return result;
                }
                result.FirstDate = rows[0].ExDate;
                result.LastDate = rows[^1].ExDate;
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
