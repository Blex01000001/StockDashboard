using StockDashboard.Models.Entities;

namespace StockDashboard.Services
{
    /// <summary>
    /// 證交所 (TWSE) 公開 API 的存取介面
    /// </summary>
    public interface ITwseApiClient
    {
        /// <summary>
        /// 下載單一股票「某個月份」的日成交資訊，並清洗成資料庫實體。
        /// 證交所回應無資料時回傳空清單；網路或解析錯誤則拋出例外，由呼叫端決定如何處理。
        /// </summary>
        Task<List<StockDailyPriceEntity>> GetMonthlyPricesAsync(string stockId, DateTime month, CancellationToken cancellationToken = default);
    }
}
