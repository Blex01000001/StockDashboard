using StockDashboard.Models.Patterns;
using StockDashboard.Services.Patterns;

namespace StockDashboard.Services
{
    public interface IPatternScanService
    {
        /// <summary>
        /// 掃描多檔股票的指定型態，每筆結果附前 daysBefore 天、後 daysAfter 天的 K 線。
        /// </summary>
        Task<PatternScanResult> ScanAsync(
            IEnumerable<string> stockIds, ICandlePattern pattern, PatternParameterValues values, int daysBefore = 10, int daysAfter = 20);
    }
}
