using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Services
{
    /// <summary>FinMind 同步進度 (Done = 已完成幾檔)</summary>
    public record FinMindSyncProgress(int Done, int Total, string StockId, string Message);

    public class FinMindSyncResult
    {
        public int StocksDone { get; set; }        // 兩個資料集都完成的股票數
        public int RowsWritten { get; set; }       // 寫入筆數 (買賣超 + 持股)
        public int Requests { get; set; }
        public bool Cancelled { get; set; }
        public bool Blocked { get; set; }          // FinMind 封鎖 IP 或達請求上限
        public string? Error { get; set; }
    }

    public interface IFinMindSyncService
    {
        Task<FinMindSyncResult> SyncAsync(IReadOnlyList<string> stockIds, bool fullReload, IProgress<FinMindSyncProgress>? progress, CancellationToken cancellationToken);
    }

    /// <summary>
    /// FinMind 個股三大法人同步：每檔股票兩個請求 (買賣超、外資持股)，一次取回整段期間。
    /// - 第一次 (或勾選完整重新下載) 從 2012-05-02 抓到今天
    /// - 之後從上次同步日往前 OverlapDays 天開始 (補上當時尚未公布或事後修正的資料)
    /// - 每次請求間隔 FinMind:RequestDelayMs (預設 2000 毫秒)
    /// - FinMind 封鎖 IP (ip banned) 或達上限時立即停止；任何錯誤也停止，已完成的股票都已寫入
    /// </summary>
    public class FinMindSyncService : IFinMindSyncService
    {
        public const string TradingDataset = "TaiwanStockInstitutionalInvestorsBuySell";
        public const string HoldingDataset = "TaiwanStockShareholding";
        public const int OverlapDays = 7;

        private readonly IFinMindApiClient _finMind;
        private readonly IInstitutionalRepository _repository;
        private readonly int _delayMs;
        private readonly Func<DateTime> _now;

        public FinMindSyncService(IFinMindApiClient finMind, IInstitutionalRepository repository, IConfiguration configuration)
            : this(finMind, repository, int.TryParse(configuration["FinMind:RequestDelayMs"], out int ms) ? ms : 2000, () => DateTime.Now) { }

        // 測試用：可指定請求間隔與「現在時間」
        public FinMindSyncService(IFinMindApiClient finMind, IInstitutionalRepository repository, int delayMs, Func<DateTime> now)
        {
            _finMind = finMind;
            _repository = repository;
            _delayMs = delayMs;
            _now = now;
        }

        /// <summary>這次要從哪一天開始抓：沒同步過或完整重新下載 → 2012-05-02；否則上次同步日往前 OverlapDays 天</summary>
        public static DateTime StartDate(string? lastSynced, bool fullReload)
        {
            if (fullReload || lastSynced == null || !DateTime.TryParse(lastSynced, out var last)) return FinMindApiClient.EarliestDate;
            var start = last.Date.AddDays(-OverlapDays);
            return start < FinMindApiClient.EarliestDate ? FinMindApiClient.EarliestDate : start;
        }

        public async Task<FinMindSyncResult> SyncAsync(IReadOnlyList<string> stockIds, bool fullReload, IProgress<FinMindSyncProgress>? progress, CancellationToken cancellationToken)
        {
            var result = new FinMindSyncResult();
            var today = _now().Date;
            var synced = new Dictionary<string, Dictionary<string, string>>
            {
                [TradingDataset] = await _repository.GetFinMindSyncedAsync(TradingDataset),
                [HoldingDataset] = await _repository.GetFinMindSyncedAsync(HoldingDataset),
            };

            for (int i = 0; i < stockIds.Count; i++)
            {
                string stockId = stockIds[i];
                foreach (var dataset in new[] { TradingDataset, HoldingDataset })
                {
                    if (cancellationToken.IsCancellationRequested) { result.Cancelled = true; return result; }

                    synced[dataset].TryGetValue(stockId, out var last);
                    var from = StartDate(last, fullReload);
                    string label = dataset == TradingDataset ? "三大法人買賣超" : "外資持股";
                    progress?.Report(new FinMindSyncProgress(i, stockIds.Count, stockId, $"下載 {stockId} {label}（{from:yyyy-MM-dd} 起）"));

                    if (result.Requests > 0 && _delayMs > 0)
                    {
                        try { await Task.Delay(_delayMs, cancellationToken); }
                        catch (OperationCanceledException) { result.Cancelled = true; return result; }
                    }

                    try
                    {
                        result.Requests++;
                        if (dataset == TradingDataset)
                        {
                            var rows = await _finMind.GetInstitutionalTradingAsync(stockId, from, today, cancellationToken);
                            if (rows.Count > 0) result.RowsWritten += await _repository.UpsertTradingAsync(rows);
                        }
                        else
                        {
                            var rows = await _finMind.GetForeignHoldingAsync(stockId, from, today, cancellationToken);
                            if (rows.Count > 0) result.RowsWritten += await _repository.UpsertHoldingAsync(rows);
                        }
                        await _repository.LogFinMindSyncAsync(stockId, dataset, today.ToString("yyyy-MM-dd"), _now());
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
                        result.Error = $"{stockId} {label}：{ex.Message}";
                        return result;
                    }
                }

                result.StocksDone++;
                progress?.Report(new FinMindSyncProgress(i + 1, stockIds.Count, stockId, $"完成 {stockId}"));
            }
            return result;
        }
    }
}
