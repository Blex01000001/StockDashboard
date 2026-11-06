using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Repositories
{
    /// <summary>
    /// 費半只是附圖：讀取失敗 (例如舊資料庫沒有這張表) 不可讓型態掃描、技術圖表失敗，附圖顯示「無資料」即可。
    /// </summary>
    public static class MarketIndexRepositoryExtensions
    {
        public static async Task<List<MarketIndexEntity>> GetAllOrEmptyAsync(this IMarketIndexRepository repository, string code)
        {
            try
            {
                return (await repository.GetAllAsync(code)).ToList();
            }
            catch (Exception)
            {
                return new List<MarketIndexEntity>();
            }
        }
    }
}
