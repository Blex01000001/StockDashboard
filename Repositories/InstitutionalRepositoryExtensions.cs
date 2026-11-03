using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Repositories
{
    /// <summary>
    /// 三大法人只是附圖：讀取失敗 (例如舊資料庫) 不可讓型態掃描、技術圖表失敗，附圖顯示「無資料」即可。
    /// </summary>
    public static class InstitutionalRepositoryExtensions
    {
        public static async Task<IEnumerable<InstitutionalDailyEntity>> GetByStockOrEmptyAsync(this IInstitutionalRepository repository, string stockId)
        {
            try
            {
                return await repository.GetByStockAsync(stockId);
            }
            catch (Exception)
            {
                return Enumerable.Empty<InstitutionalDailyEntity>();
            }
        }
    }
}
