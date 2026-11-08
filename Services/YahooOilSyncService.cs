using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Services
{
    public interface IYahooOilSyncService
    {
        Task<OilPriceSyncResult> SyncAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Yahoo Finance 的 Brent 期貨 (BZ=F) 同步：一次請求取回 2007-07-30 起的全部日線，把「收盤價」寫入 OilPriceDaily
    /// (代號 <see cref="Code"/>，與 oilpriceapi 的 BRENT_CRUDE_USD 分開存；同日覆蓋)。
    /// 目的是讓使用者在頁面並排比較兩個來源的差異 (oilpriceapi 是當日均價，Yahoo 是收盤價)。
    /// 用 period1/period2 才會是日線 (range=max 會降成月線)。
    /// </summary>
    public class YahooOilSyncService : IYahooOilSyncService
    {
        public const string Code = "BRENT_YAHOO_BZF";   // 資料庫代號
        public const string Symbol = "BZ=F";             // Yahoo 代號
        public static readonly DateTime FirstDate = new(2007, 7, 30);   // BZ=F 最早的交易日

        private readonly IMarketIndexApiClient _api;
        private readonly IOilPriceRepository _repository;
        private readonly Func<DateTime> _utcNow;

        public YahooOilSyncService(IMarketIndexApiClient api, IOilPriceRepository repository)
            : this(api, repository, () => DateTime.UtcNow) { }

        // 測試用：可指定「現在時間」
        public YahooOilSyncService(IMarketIndexApiClient api, IOilPriceRepository repository, Func<DateTime> utcNow)
        {
            _api = api;
            _repository = repository;
            _utcNow = utcNow;
        }

        public async Task<OilPriceSyncResult> SyncAsync(CancellationToken cancellationToken)
        {
            var result = new OilPriceSyncResult();
            try
            {
                var bars = await _api.GetDailyAsync(Code, Symbol, FirstDate, _utcNow().Date, cancellationToken);
                if (bars.Count == 0)
                {
                    result.Error = "Yahoo 沒有回傳 Brent 期貨日線資料";
                    return result;
                }
                var rows = bars.Select(b => new OilPriceEntity { Code = Code, TradeDate = b.TradeDate, Price = b.Close }).ToList();
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
