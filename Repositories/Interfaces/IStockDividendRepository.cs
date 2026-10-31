using StockDashboard.Models.Entities;

namespace StockDashboard.Repositories.Interfaces
{
    public interface IStockDividendRepository
    {
        // 取得單一股票所有的歷史除權息紀錄 (依照年份排序)
        Task<IEnumerable<StockDividendEntity>> GetDividendsByStockIdAsync(string stockId);

        // 取得特定年份所有公司的除權息資料
        Task<IEnumerable<StockDividendEntity>> GetDividendsByYearAsync(string year);
    }
}
