using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Services
{
    /// <summary>除權息資料來源。實際的 API Client (目前是 FinMind) 實作此介面，回傳與來源無關的事件。</summary>
    public interface IStockCorporateActionApiClient
    {
        // 取得單一股票的除權息事件 (整段歷史，由呼叫端決定要寫入哪些)
        Task<List<StockCorporateActionEntity>> GetActionsAsync(string stockId, CancellationToken cancellationToken = default);
    }

    /// <summary>同步進度 (Done = 已完成幾檔)</summary>
    public record StockCorporateActionSyncProgress(int Done, int Total, string StockId, string Message);

    public class StockCorporateActionSyncResult
    {
        public int StocksDone { get; set; }           // 完成的股票數 (含沒有事件的)
        public int StocksWithoutEvents { get; set; }  // 其中來源沒有任何除權息資料的檔數
        public int RowsWritten { get; set; }
        public int Requests { get; set; }
        public bool Cancelled { get; set; }
        public bool Blocked { get; set; }             // FinMind 封鎖 IP 或達請求上限
        public string? Error { get; set; }
    }

    public interface IStockCorporateActionSyncService
    {
        Task<StockCorporateActionSyncResult> SyncAsync(IReadOnlyList<string> stockIds, IProgress<StockCorporateActionSyncProgress>? progress, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 除權息同步：逐檔一個請求取回整段歷史，寫入 StockCorporateAction (同日覆蓋，舊資料不刪)。
    /// - 每次請求間隔 FinMind:RequestDelayMs (預設 2000 毫秒)
    /// - 封鎖 / 達上限 (FinMindBlockedException) 或任何錯誤立即停止，已完成的股票都已寫入
    /// - 從未除權息的股票沒有資料是正常的，不算錯誤
    /// </summary>
    public class StockCorporateActionSyncService : IStockCorporateActionSyncService
    {
        private readonly IStockCorporateActionApiClient _api;
        private readonly IStockCorporateActionRepository _repository;
        private readonly int _delayMs;

        public StockCorporateActionSyncService(IStockCorporateActionApiClient api, IStockCorporateActionRepository repository, IConfiguration configuration)
            : this(api, repository, int.TryParse(configuration["FinMind:RequestDelayMs"], out int ms) ? ms : 2000) { }

        // 測試用：可指定請求間隔
        public StockCorporateActionSyncService(IStockCorporateActionApiClient api, IStockCorporateActionRepository repository, int delayMs)
        {
            _api = api;
            _repository = repository;
            _delayMs = delayMs;
        }

        public async Task<StockCorporateActionSyncResult> SyncAsync(IReadOnlyList<string> stockIds, IProgress<StockCorporateActionSyncProgress>? progress, CancellationToken cancellationToken)
        {
            var result = new StockCorporateActionSyncResult();
            for (int i = 0; i < stockIds.Count; i++)
            {
                string stockId = stockIds[i];
                if (cancellationToken.IsCancellationRequested) { result.Cancelled = true; return result; }
                progress?.Report(new StockCorporateActionSyncProgress(i, stockIds.Count, stockId, $"下載 {stockId} 除權息"));

                if (result.Requests > 0 && _delayMs > 0)
                {
                    try { await Task.Delay(_delayMs, cancellationToken); }
                    catch (OperationCanceledException) { result.Cancelled = true; return result; }
                }

                try
                {
                    result.Requests++;
                    // 壞資料 (前收盤或參考價 <= 0、沒有日期) 不寫入；代號一律以要求的為準
                    var rows = (await _api.GetActionsAsync(stockId, cancellationToken))
                        .Where(a => a.PreClose > 0 && a.RefPrice > 0 && !string.IsNullOrEmpty(a.ExDate))
                        .Select(a => new StockCorporateActionEntity { StockId = stockId, ExDate = a.ExDate, PreClose = a.PreClose, RefPrice = a.RefPrice })
                        .ToList();
                    if (rows.Count > 0) result.RowsWritten += await _repository.UpsertAsync(rows);
                    else result.StocksWithoutEvents++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    return result;
                }
                catch (FinMindBlockedException ex)
                {
                    result.Blocked = true;
                    result.Error = ex.Message;
                    return result;
                }
                catch (Exception ex)
                {
                    result.Error = $"{stockId}：{ex.Message}";
                    return result;
                }

                result.StocksDone++;
                progress?.Report(new StockCorporateActionSyncProgress(i + 1, stockIds.Count, stockId, $"完成 {stockId}"));
            }
            return result;
        }
    }
}
