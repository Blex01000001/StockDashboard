using StockDashboard.Models;

namespace StockDashboard.Repositories.Interfaces
{
    /// <summary>
    /// 依產業別查股票代號：讀使用者既有的 StockList 的「產業別」欄位 (代碼，例如 24 = 半導體業)，唯讀。
    /// 由 StockNameRepository 實作 (StockList 的唯一讀取入口)。
    /// </summary>
    public interface IIndustryRepository
    {
        // StockList 裡有的全部產業別 (代碼兩位數、名稱、股票檔數)，依代碼排序；沒有 StockList 或產業別欄位時回傳空清單 (下拉選單用)
        Task<List<IndustryOption>> GetIndustriesAsync();

        // 某產業別 (代碼，忽略前導 0，例如 "24"、"01" 與 "1") 的全部股票代號，依代號排序；沒有 StockList 或產業別欄位時回傳空清單
        Task<List<string>> GetStockIdsByIndustryAsync(string industryCode);
    }
}
