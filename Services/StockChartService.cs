using StockDashboard.Models;
using StockDashboard.Models.Patterns;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Interfaces;

namespace StockDashboard.Services
{
    /// <summary>技術圖表頁的資料：完整日線 (重算指標用) + 畫在圖上的最近 N 根 (含均線、指標、三大法人)</summary>
    public class StockChartData
    {
        public string Label { get; set; } = string.Empty;              // 圖表標題，例如 "2330 台積電"
        public List<StockDataPoint> FullCandles { get; set; } = new();  // 由舊到新
        public int WindowStart { get; set; }                            // Candles 第一根在 FullCandles 的位置
        public List<PatternCandle> Candles { get; set; } = new();
    }

    public interface IStockChartService
    {
        // 查無日線時回傳 null
        Task<StockChartData?> LoadAsync(string stockId, int count, IndicatorSettings indicators);
    }

    /// <summary>
    /// 技術圖表：指標與均線用完整歷史計算 (圖最左邊的 MA120 等才正確)，只畫最近 count 根。
    /// 計算方式與型態掃描小卡相同 (共用 PatternScanService 的靜態方法)。
    /// </summary>
    public class StockChartService : IStockChartService
    {
        private readonly IStockPriceRepository _priceRepository;
        private readonly IStockNameRepository _stockNameRepository;
        private readonly IInstitutionalRepository _institutionalRepository;

        public StockChartService(IStockPriceRepository priceRepository, IStockNameRepository stockNameRepository, IInstitutionalRepository institutionalRepository)
        {
            _priceRepository = priceRepository;
            _stockNameRepository = stockNameRepository;
            _institutionalRepository = institutionalRepository;
        }

        public async Task<StockChartData?> LoadAsync(string stockId, int count, IndicatorSettings indicators)
        {
            var full = (await _priceRepository.GetAllCandlesAsync(stockId)).ToList();
            if (full.Count == 0) return null;

            string? name = await _stockNameRepository.GetStockNameOrNullAsync(stockId);
            var data = Build(full, count, indicators, await _institutionalRepository.GetByStockOrEmptyAsync(stockId));
            data.Label = name == null ? stockId : $"{stockId} {name}";
            return data;
        }

        public static StockChartData Build(List<StockDataPoint> full, int count, IndicatorSettings indicators, IEnumerable<Models.Entities.InstitutionalDailyEntity> institutional)
        {
            int start = Math.Max(0, full.Count - count);
            var data = new StockChartData
            {
                FullCandles = full,
                WindowStart = start,
                Candles = full.Skip(start).Select(PatternCandle.From).ToList()
            };
            Recompute(data, indicators);
            PatternScanService.ApplyInstitutional(data.Candles, institutional);
            return data;
        }

        // 調整指標參數後重算 (不查資料庫)
        public static void Recompute(StockChartData data, IndicatorSettings indicators) =>
            PatternScanService.ApplyIndicators(new[] { (data.WindowStart, (IReadOnlyList<PatternCandle>)data.Candles) }, data.FullCandles, indicators);
    }
}
