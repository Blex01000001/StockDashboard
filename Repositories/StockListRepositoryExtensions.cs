using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Repositories
{
    /// <summary>
    /// 「顯示用」的股票名稱查詢。
    /// 名稱只是附加資訊，查不到或資料表有問題 (例如結構不符) 時，一律退回只顯示代號，
    /// 不可讓查詢、K 線圖、多檔比較等主要功能因此失敗。
    /// </summary>
    public static class StockListRepositoryExtensions
    {
        public static async Task<string?> GetStockNameOrNullAsync(this IStockListRepository repository, string stockId)
        {
            try
            {
                return (await repository.GetStockByIdAsync(stockId))?.StockName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static async Task<Dictionary<string, string>> GetStockNamesOrEmptyAsync(this IStockListRepository repository, IEnumerable<string> stockIds)
        {
            try
            {
                return await repository.GetStockNamesAsync(stockIds);
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }
    }
}
