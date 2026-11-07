using StockDashboard.Models.Entities;

namespace StockDashboard.Repositories.Interfaces
{
    public interface IStockCorporateActionRepository
    {
        // 寫入 (同股票同除權息日覆蓋)，回傳寫入筆數
        Task<int> UpsertAsync(IEnumerable<StockCorporateActionEntity> rows);

        // 單一股票全部除權息事件 (由舊到新)
        Task<IEnumerable<StockCorporateActionEntity>> GetByStockIdAsync(string stockId);

        // 已有資料的股票數與事件筆數 (資料更新頁顯示)
        Task<(int Stocks, int Events)> GetSummaryAsync();
    }
}
