using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Repositories
{
    /// <summary>
    /// 原油只是附圖：讀取失敗 (例如舊資料庫沒有這張表) 不可讓型態掃描、技術圖表失敗，附圖顯示「無資料」即可。
    /// </summary>
    public static class OilPriceRepositoryExtensions
    {
        public static async Task<List<OilPriceEntity>> GetAllOrEmptyAsync(this IOilPriceRepository repository, string code)
        {
            try
            {
                return (await repository.GetAllAsync(code)).ToList();
            }
            catch (Exception)
            {
                return new List<OilPriceEntity>();
            }
        }
    }
}
