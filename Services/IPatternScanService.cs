using StockDashboard.Models.Patterns;

namespace StockDashboard.Services
{
    public interface IPatternScanService
    {
        /// <summary>
        /// 掃描多檔股票的看漲吞沒型態，每筆結果附前 daysBefore 天、後 daysAfter 天的 K 線。
        /// </summary>
        Task<PatternScanResult> ScanBullishEngulfingAsync(
            IEnumerable<string> stockIds, BullishEngulfingOptions options, int daysBefore = 10, int daysAfter = 20);
    }
}
