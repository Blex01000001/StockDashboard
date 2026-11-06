using StockDashboard.Models.Entities;

namespace StockDashboard.Repositories.Interfaces
{
    public interface IMarketIndexRepository
    {
        // 寫入 (同代號同日期覆蓋)，回傳寫入筆數
        Task<int> UpsertAsync(IEnumerable<MarketIndexEntity> rows);

        // 某代號全部 K 線 (由舊到新)
        Task<IEnumerable<MarketIndexEntity>> GetAllAsync(string code);

        // 已有資料的日期範圍與天數 (資料更新頁顯示)
        Task<(string? FirstDate, string? LastDate, int Days)> GetSummaryAsync(string code);
    }
}
