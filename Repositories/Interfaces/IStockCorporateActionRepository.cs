using StockDashboard.Models.Entities;

namespace StockDashboard.Repositories.Interfaces
{
    public interface IStockCorporateActionRepository
    {
        // 取得單一股票的歷史重大事件
        Task<IEnumerable<StockCorporateActionEntity>> GetCorporateActionsAsync(string stockId);

        // 依據事件類型 (ActionType) 過濾
        Task<IEnumerable<StockCorporateActionEntity>> GetCorporateActionsByTypeAsync(string stockId, string actionType);
    }
}
