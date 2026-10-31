using StockDashboard.Models;

namespace StockDashboard.Services
{
    public interface IStockPerformanceService
    {
        Task<CompareChartResult> GetCompareChartDataAsync(List<string> stockIds, int days, int rangeDay = 660);
    }
}
