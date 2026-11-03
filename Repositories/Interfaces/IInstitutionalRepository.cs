using StockDashboard.Models.Entities;

namespace StockDashboard.Repositories.Interfaces
{
    public interface IInstitutionalRepository
    {
        // 寫入 T86 買賣超欄位 (不影響持股欄位)，回傳寫入筆數
        Task<int> UpsertTradingAsync(IEnumerable<InstitutionalDailyEntity> rows);

        // 寫入 MI_QFIIS 持股欄位 (不影響買賣超欄位)，回傳寫入筆數
        Task<int> UpsertHoldingAsync(IEnumerable<InstitutionalDailyEntity> rows);

        // 記錄某天某來源已查過
        Task LogCheckAsync(string tradeDate, string source, bool hasData, DateTime checkedAt);

        // 某來源已查過的日期：日期 → (是否有資料, 查詢時間)
        Task<Dictionary<string, (bool HasData, DateTime CheckedAt)>> GetCheckedDatesAsync(string source);

        // 單檔股票全部三大法人資料 (由舊到新)
        Task<IEnumerable<InstitutionalDailyEntity>> GetByStockAsync(string stockId);

        // 已有資料的日期範圍與天數 (資料更新頁顯示)
        Task<(string? FirstDate, string? LastDate, int Days)> GetSummaryAsync();
    }
}
