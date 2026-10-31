using StockDashboard.Models.Entities;

namespace StockDashboard.Repositories.Interfaces
{
    public interface IStockListRepository
    {
        // 取得所有上市上櫃公司清單 (通常用於下拉選單或全域快取)
        Task<IEnumerable<StockListEntity>> GetAllStocksAsync();

        // 依據代號取得單一公司詳細資料
        Task<StockListEntity> GetStockByIdAsync(string stockId);
    }
}
