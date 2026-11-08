using StockDashboard.Models;
using StockDashboard.Models.Entities;
using StockDashboard.Models.Patterns;
using StockDashboard.Repositories.Interfaces;
using StockDashboard.Services;
using StockDashboard.Services.Patterns;

namespace StockDashboard.Tests;

/// <summary>PatternScanService.ScanAsync、StockChartService.LoadAsync 的整體流程 (用假的 Repository)</summary>
internal static class FakeRepos
{
    // 每筆日期 = 2026-01-01 起算。dojiDays 之外都是十字線；engulfAt 的前一根黑 K、該根紅 K 吞沒 → 看漲吞沒成立於 engulfAt
    public static List<StockDataPoint> Series(int count, params int[] engulfAt)
    {
        var list = Enumerable.Range(0, count).Select(i => new StockDataPoint
        {
            Date = new DateTime(2026, 1, 1).AddDays(i).ToString("yyyy-MM-dd"),
            Open = 100, High = 100, Low = 100, Close = 100, Volume = 1000
        }).ToList();
        foreach (int i in engulfAt)
        {
            list[i - 1] = new StockDataPoint { Date = list[i - 1].Date, Open = 100, Close = 98, High = 100, Low = 98, Volume = 1000 };
            list[i] = new StockDataPoint { Date = list[i].Date, Open = 97, Close = 101, High = 101, Low = 97, Volume = 1000 };
        }
        return list;
    }

    public sealed class Price(Dictionary<string, List<StockDataPoint>> data) : IStockPriceRepository
    {
        public Task<IEnumerable<StockDataPoint>> GetAllCandlesAsync(string stockId) =>
            Task.FromResult<IEnumerable<StockDataPoint>>(data.TryGetValue(stockId, out var l) ? l : new List<StockDataPoint>());
        public Task<IEnumerable<StockDailyPriceEntity>> GetHistoricalPricesAsync(string stockId, string startDate, string endDate) => throw new NotSupportedException();
        public Task<IEnumerable<StockDailyPriceEntity>> GetMultipleHistoricalPricesAsync(IEnumerable<string> stockIds, string startDate, string endDate) => throw new NotSupportedException();
        public Task<int> UpsertPricesAsync(IEnumerable<StockDailyPriceEntity> prices) => throw new NotSupportedException();
    }

    public sealed class Name(Dictionary<string, string> names, bool fail = false) : IStockNameRepository
    {
        public Task<Dictionary<string, string>> GetStockNamesAsync(IEnumerable<string> stockIds) =>
            fail ? throw new InvalidOperationException("no such table") : Task.FromResult(names.Where(kv => stockIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
        public Task<string?> GetStockNameAsync(string stockId) =>
            fail ? throw new InvalidOperationException("no such table") : Task.FromResult(names.GetValueOrDefault(stockId));
        public Task<IEnumerable<StockNameEntity>> GetAllStocksAsync() => throw new NotSupportedException();
        public Task<StockNameSummary> GetSummaryAsync() => throw new NotSupportedException();
        public Task<int> UpsertStocksAsync(IEnumerable<StockNameEntity> stocks) => throw new NotSupportedException();
    }

    public sealed class Inst(bool fail = false) : IInstitutionalRepository
    {
        public Task<IEnumerable<InstitutionalDailyEntity>> GetByStockAsync(string stockId) =>
            fail ? throw new InvalidOperationException("no such table")
                 : Task.FromResult<IEnumerable<InstitutionalDailyEntity>>(new[] { new InstitutionalDailyEntity { StockId = stockId, TradeDate = "2026-01-12", TotalNet = 7 } });
        public Task<int> UpsertTradingAsync(IEnumerable<InstitutionalDailyEntity> rows) => throw new NotSupportedException();
        public Task<int> UpsertHoldingAsync(IEnumerable<InstitutionalDailyEntity> rows) => throw new NotSupportedException();
        public Task LogCheckAsync(string tradeDate, string source, bool hasData, DateTime checkedAt) => throw new NotSupportedException();
        public Task<Dictionary<string, (bool HasData, DateTime CheckedAt)>> GetCheckedDatesAsync(string source) => throw new NotSupportedException();
        public Task LogFinMindSyncAsync(string stockId, string dataset, string lastDate, DateTime checkedAt) => throw new NotSupportedException();
        public Task<Dictionary<string, string>> GetFinMindSyncedAsync(string dataset) => throw new NotSupportedException();
        public Task<(string? FirstDate, string? LastDate, int Days)> GetSummaryAsync() => throw new NotSupportedException();
    }

    public sealed class Oil(bool fail = false) : IOilPriceRepository
    {
        public Task<IEnumerable<OilPriceEntity>> GetAllAsync(string code) =>
            fail ? throw new InvalidOperationException("no such table") : Task.FromResult<IEnumerable<OilPriceEntity>>(new List<OilPriceEntity>());
        public Task<int> UpsertAsync(IEnumerable<OilPriceEntity> rows) => throw new NotSupportedException();
        public Task<(string? FirstDate, string? LastDate, int Days)> GetSummaryAsync(string code) => throw new NotSupportedException();
    }

    public sealed class Actions(List<StockCorporateActionEntity>? rows = null) : IStockCorporateActionRepository
    {
        public Task<IEnumerable<StockCorporateActionEntity>> GetByStockIdAsync(string stockId) =>
            Task.FromResult<IEnumerable<StockCorporateActionEntity>>((rows ?? new()).Where(a => a.StockId == stockId).ToList());
        public Task<int> UpsertAsync(IEnumerable<StockCorporateActionEntity> r) => throw new NotSupportedException();
        public Task<(int Stocks, int Events)> GetSummaryAsync() => throw new NotSupportedException();
    }

    public sealed class Market(bool fail = false) : IMarketIndexRepository
    {
        public Task<IEnumerable<MarketIndexEntity>> GetAllAsync(string code) =>
            fail ? throw new InvalidOperationException("no such table") : Task.FromResult<IEnumerable<MarketIndexEntity>>(new List<MarketIndexEntity>());
        public Task<int> UpsertAsync(IEnumerable<MarketIndexEntity> rows) => throw new NotSupportedException();
        public Task<(string? FirstDate, string? LastDate, int Days)> GetSummaryAsync(string code) => throw new NotSupportedException();
    }
}

public class PatternScanServiceFlowTests
{
    private static PatternScanService Create(Dictionary<string, List<StockDataPoint>> prices, bool failNames = false, bool failExtras = false, List<StockCorporateActionEntity>? actions = null) =>
        new(new FakeRepos.Price(prices), new FakeRepos.Name(new() { ["A"] = "甲公司", ["B"] = "乙公司" }, failNames),
            new FakeRepos.Inst(failExtras), new FakeRepos.Oil(failExtras), new FakeRepos.Market(failExtras), new FakeRepos.Actions(actions));

    private static readonly BullishEngulfingPattern Pattern = new();
    private static PatternParameterValues Values() => K.NoTrend(Pattern);

    private static Dictionary<string, List<StockDataPoint>> Data() => new()
    {
        ["A"] = FakeRepos.Series(40, 1, 11),
        ["B"] = FakeRepos.Series(40, 11),
        ["C"] = FakeRepos.Series(40),     // 有資料但沒有型態
    };

    [Fact]
    public async Task 掃描_新日期在前_同日依代號_無資料與無型態分開處理()
    {
        var result = await Create(Data()).ScanAsync(new[] { "A", "B", "C", "9999" }, Pattern, Values());

        Assert.Equal(new[] { ("2026-01-12", "A"), ("2026-01-12", "B"), ("2026-01-02", "A") },
            result.Matches.Select(m => (m.SignalDate, m.StockId)));
        Assert.Equal(new[] { "9999" }, result.StocksWithoutData);
        Assert.Equal(new[] { "A", "B" }, result.FullCandles.Keys.OrderBy(k => k));   // 沒有型態的 C 不保留完整日線
        Assert.Equal(("甲公司", "乙公司"), (result.Matches[0].StockName, result.Matches[1].StockName));
        Assert.True(result.IsBullish);
        Assert.Equal(3, result.WinRates.Count);       // 預設觀察天數 5／10／20
    }

    [Fact]
    public async Task 掃描_重複代號只算一次()
    {
        var result = await Create(Data()).ScanAsync(new[] { "B", "B" }, Pattern, Values());
        Assert.Single(result.Matches);
    }

    [Fact]
    public async Task 掃描_名稱與附加資料查詢失敗_不影響結果()
    {
        var result = await Create(Data(), failNames: true, failExtras: true).ScanAsync(new[] { "A" }, Pattern, Values());

        Assert.Equal(2, result.Matches.Count);
        Assert.All(result.Matches, m => Assert.Null(m.StockName));
        Assert.All(result.Matches.SelectMany(m => m.Candles), c => Assert.Null(c.TotalNet));   // 三大法人讀不到 → 留空
    }

    [Fact]
    public async Task 掃描_三大法人依日期填入小卡()
    {
        var result = await Create(Data()).ScanAsync(new[] { "B" }, Pattern, Values());
        var signalCandle = result.Matches[0].Candles.Single(c => c.Date == "2026-01-12");
        Assert.Equal(7, signalCandle.TotalNet);
    }

    // 2026-01-12 除權息，參考價只有前收盤的一半：之前的價格減半，原本 01-12 的吞沒 (開 97 收 101) 就不再吞沒前一天的黑 K
    private static List<StockCorporateActionEntity> HalfAction(string stockId) =>
        new() { new StockCorporateActionEntity { StockId = stockId, ExDate = "2026-01-12", PreClose = 100, RefPrice = 50 } };

    [Fact]
    public async Task 掃描_還原股價開關_只有adjusted時用還原後的價格判斷()
    {
        var service = Create(Data(), actions: HalfAction("B"));

        var raw = await service.ScanAsync(new[] { "B" }, Pattern, Values());
        var adjusted = await service.ScanAsync(new[] { "B" }, Pattern, Values(), adjusted: true);

        Assert.False(raw.Adjusted);
        Assert.Single(raw.Matches);
        Assert.True(adjusted.Adjusted);
        Assert.Empty(adjusted.Matches);                       // 還原後型態不成立
    }

    [Fact]
    public async Task 掃描_還原股價_沒有除權息事件的股票結果不變()
    {
        var service = Create(Data(), actions: HalfAction("B"));

        var raw = await service.ScanAsync(new[] { "A" }, Pattern, Values());
        var adjusted = await service.ScanAsync(new[] { "A" }, Pattern, Values(), adjusted: true);

        Assert.Equal(raw.Matches.Select(m => m.SignalDate), adjusted.Matches.Select(m => m.SignalDate));
        Assert.Equal(raw.Matches[0].Candles.Select(c => c.Close), adjusted.Matches[0].Candles.Select(c => c.Close));
    }

    [Fact]
    public async Task 掃描_自訂觀察天數與小卡範圍()
    {
        var result = await Create(Data()).ScanAsync(new[] { "A" }, Pattern, Values(), daysBefore: 3, daysAfter: 5, horizons: new[] { 2, 2, 0 });

        Assert.Equal(new[] { 2 }, result.Horizons);                     // 去除重複與非正數
        Assert.Equal((3, 5), (result.DaysBefore, result.DaysAfter));
        var first = result.Matches.Last();                              // 2026-01-02 (index 1)：前面只有 1 根
        Assert.Equal(1, first.SignalIndex);
        Assert.Equal(7, first.Candles.Count);                           // index 0..6
    }
}

public class StockChartServiceLoadTests
{
    private static StockChartService Create(Dictionary<string, List<StockDataPoint>> prices, bool failNames = false, bool failExtras = false, List<StockCorporateActionEntity>? actions = null) =>
        new(new FakeRepos.Price(prices), new FakeRepos.Name(new() { ["A"] = "甲公司" }, failNames),
            new FakeRepos.Inst(failExtras), new FakeRepos.Oil(failExtras), new FakeRepos.Market(failExtras), new FakeRepos.Actions(actions));

    [Fact]
    public async Task 查無日線回傳Null()
    {
        Assert.Null(await Create(new()).LoadAsync("9999", 120, new IndicatorSettings()));
    }

    [Fact]
    public async Task 載入_標題含名稱_只取最近N根_並填入三大法人()
    {
        var data = await Create(new() { ["A"] = FakeRepos.Series(40, 11) }).LoadAsync("A", 15, new IndicatorSettings());

        Assert.NotNull(data);
        Assert.Equal("A 甲公司", data!.Label);
        Assert.Equal((25, 15, 40), (data.WindowStart, data.Candles.Count, data.FullCandles.Count));
        Assert.Equal("2026-01-26", data.Candles[0].Date);                 // 最近 15 根從 index 25 開始
        Assert.DoesNotContain(data.Candles, c => c.TotalNet != null);     // 假的三大法人資料在 01-12，不在這個範圍內

        var all = await Create(new() { ["A"] = FakeRepos.Series(40, 11) }).LoadAsync("A", 0, new IndicatorSettings());
        Assert.Equal(7, all!.Candles.Single(c => c.Date == "2026-01-12").TotalNet);
    }

    [Fact]
    public async Task 載入_還原股價_標題標示且事件日之前的價格調整()
    {
        var actions = new List<StockCorporateActionEntity> { new() { StockId = "A", ExDate = "2026-01-12", PreClose = 100, RefPrice = 50 } };
        var prices = new Dictionary<string, List<StockDataPoint>> { ["A"] = FakeRepos.Series(40) };

        var raw = await Create(prices, actions: actions).LoadAsync("A", 0, new IndicatorSettings());
        var adj = await Create(prices, actions: actions).LoadAsync("A", 0, new IndicatorSettings(), adjusted: true);

        Assert.False(raw!.Adjusted);
        Assert.Equal("A 甲公司", raw.Label);
        Assert.Equal(100.0, raw.FullCandles[0].Close);
        Assert.True(adj!.Adjusted);
        Assert.Equal("A 甲公司（還原）", adj.Label);
        Assert.Equal(50.0, adj.FullCandles[0].Close);           // 事件日之前減半
        Assert.Equal(100.0, adj.FullCandles[^1].Close);         // 事件日之後不變
    }

    [Fact]
    public async Task 名稱查不到或附加資料失敗_仍可載入_標題只有代號()
    {
        var data = await Create(new() { ["A"] = FakeRepos.Series(40) }, failNames: true, failExtras: true).LoadAsync("A", 0, new IndicatorSettings());

        Assert.NotNull(data);
        Assert.Equal("A", data!.Label);
        Assert.Equal(40, data.Candles.Count);
        Assert.Empty(data.Institutional);
    }
}
