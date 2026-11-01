using StockDashboard.Models.Entities;

namespace StockDashboard.Repositories.Interfaces
{
    public interface IStockListRepository
    {
        // 取得所有股票清單 (依代號排序，用於輸入框的自動完成選單)
        Task<IEnumerable<StockListEntity>> GetAllStocksAsync();

        // 依據代號取得單一股票；查無資料回傳 null
        Task<StockListEntity?> GetStockByIdAsync(string stockId);

        // 一次查詢多檔股票的名稱：回傳 代號 → 名稱，查無的代號不會出現在結果中
        Task<Dictionary<string, string>> GetStockNamesAsync(IEnumerable<string> stockIds);

        // 取得清單筆數與最後更新時間 (清單為空時 LastUpdatedAt 為 null)
        Task<(int Count, string? LastUpdatedAt)> GetSummaryAsync();

        // 檢查 StockList 資料表結構；正常回傳 null，結構不符時回傳說明文字
        Task<string?> GetSchemaProblemAsync();

        // 新增或更新股票清單 (以 StockId 為主鍵)，回傳寫入筆數
        Task<int> UpsertStocksAsync(IEnumerable<StockListEntity> stocks);
    }
}
