using StockDashboard.Models;
using StockDashboard.Models.Entities;
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
        public List<InstitutionalDailyEntity> Institutional { get; set; } = new();   // 改顯示筆數時重填用
        public int WindowStart { get; set; }                            // Candles 第一根在 FullCandles 的位置
        public List<PatternCandle> Candles { get; set; } = new();
    }

    public interface IStockChartService
    {
        // 查無日線時回傳 null；count = 0 表示全部
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

        public static StockChartData Build(List<StockDataPoint> full, int count, IndicatorSettings indicators, IEnumerable<InstitutionalDailyEntity> institutional)
        {
            var data = new StockChartData { FullCandles = full, Institutional = institutional.ToList() };
            SetWindow(data, count, indicators);
            return data;
        }

        /// <summary>改變顯示筆數 (count = 0 為全部)：重切範圍並重填指標與三大法人，不查資料庫</summary>
        public static void SetWindow(StockChartData data, int count, IndicatorSettings indicators)
        {
            int start = count <= 0 ? 0 : Math.Max(0, data.FullCandles.Count - count);
            data.WindowStart = start;
            data.Candles = data.FullCandles.Skip(start).Select(PatternCandle.From).ToList();
            Recompute(data, indicators);
            PatternScanService.ApplyInstitutional(data.Candles, data.Institutional);
        }

        /// <summary>
        /// 能顯示到 date 的最小筆數選項 (由小到大的 options，0 = 全部)；用於從型態掃描小卡跳過來時，確保型態日在圖上。
        /// </summary>
        public static int CountToInclude(IReadOnlyList<StockDataPoint> full, string date, IEnumerable<int> options, int preferred)
        {
            int index = full.Count - 1;
            while (index >= 0 && string.CompareOrdinal(full[index].Date, date) > 0) index--;
            int needed = full.Count - Math.Max(0, index);   // 從這一天到最後的根數
            // 前面再多留一些 K 棒，型態前的走勢才看得到
            needed += 30;
            if (preferred <= 0 || preferred >= needed) return preferred;
            foreach (var n in options.Where(n => n > 0).OrderBy(n => n))
                if (n >= needed) return n;
            return 0;
        }

        // 調整指標參數後重算 (不查資料庫)
        public static void Recompute(StockChartData data, IndicatorSettings indicators) =>
            PatternScanService.ApplyIndicators(new[] { (data.WindowStart, (IReadOnlyList<PatternCandle>)data.Candles) }, data.FullCandles, indicators);
    }
}
