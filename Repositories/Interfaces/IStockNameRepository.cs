using StockDashboard.Models;
using StockDashboard.Models.Entities;

namespace StockDashboard.Repositories.Interfaces
{
    /// <summary>
    /// 股票名稱查詢。名稱有兩個來源，合併後提供：
    /// 1. StockList     ─ 使用者既有的「上市公司基本資料」表 (中文欄位)，取「公司代號 → 公司簡稱」，唯讀、不修改
    /// 2. StockNameList ─ 由「更新股票清單」從證交所同步的代號清單 (含 ETF)
    /// 同一代號兩邊都有時，以 StockList 的公司簡稱為準。
    /// </summary>
    public interface IStockNameRepository
    {
        // 取得所有股票代號與名稱 (合併兩個來源，依代號排序，用於自動完成選單)
        Task<IEnumerable<StockNameEntity>> GetAllStocksAsync();

        // 取得單一股票名稱；查無回傳 null
        Task<string?> GetStockNameAsync(string stockId);

        // 一次查詢多檔股票的名稱：回傳 代號 → 名稱，查無的代號不會出現在結果中
        Task<Dictionary<string, string>> GetStockNamesAsync(IEnumerable<string> stockIds);

        // 兩個來源的筆數與最後同步時間 (顯示在資料更新頁)
        Task<StockNameSummary> GetSummaryAsync();

        // 新增或更新 StockNameList (以 StockId 為主鍵)，回傳寫入筆數；不會動到 StockList
        Task<int> UpsertStocksAsync(IEnumerable<StockNameEntity> stocks);
    }
}
