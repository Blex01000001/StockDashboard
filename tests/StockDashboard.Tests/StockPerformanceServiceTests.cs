using StockDashboard.Models;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories.Interfaces;
using StockDashboard.Services;

namespace StockDashboard.Tests;

public class StockPerformanceServiceTests
{
    private sealed class FakePriceRepository(params StockDailyPriceEntity[] rows) : IStockPriceRepository
    {
        public Task<IEnumerable<StockDailyPriceEntity>> GetMultipleHistoricalPricesAsync(IEnumerable<string> stockIds, string startDate, string endDate) =>
            Task.FromResult(rows.Where(r => stockIds.Contains(r.StockId)));

        public Task<IEnumerable<StockDailyPriceEntity>> GetHistoricalPricesAsync(string stockId, string startDate, string endDate) => throw new NotSupportedException();
        public Task<IEnumerable<StockDataPoint>> GetLatestCandlesAsync(string stockId, int count) => throw new NotSupportedException();
        public Task<StockDailyPriceEntity?> GetLatestPriceAsync(string stockId) => throw new NotSupportedException();
        public Task<int> UpsertPricesAsync(IEnumerable<StockDailyPriceEntity> prices) => throw new NotSupportedException();
    }

    private sealed class FakeNameRepository(Dictionary<string, string>? names, bool fail = false) : IStockNameRepository
    {
        public Task<Dictionary<string, string>> GetStockNamesAsync(IEnumerable<string> stockIds) =>
            fail ? throw new InvalidOperationException("no such column: StockId")
                 : Task.FromResult(names!.Where(kv => stockIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));

        public Task<IEnumerable<StockNameEntity>> GetAllStocksAsync() => throw new NotSupportedException();
        public Task<string?> GetStockNameAsync(string stockId) => throw new NotSupportedException();
        public Task<StockNameSummary> GetSummaryAsync() => throw new NotSupportedException();
        public Task<int> UpsertStocksAsync(IEnumerable<StockNameEntity> stocks) => throw new NotSupportedException();
    }

    private static StockDailyPriceEntity P(string id, string date, double? close) => new() { StockId = id, TradeDate = date, ClosePrice = close };

    private static StockPerformanceService Service(IStockPriceRepository prices, IStockNameRepository? names = null) =>
        new(prices, names ?? new FakeNameRepository(new()));

    [Fact]
    public async Task 報酬率為今日收盤相對N個交易日前收盤的百分比()
    {
        var prices = new FakePriceRepository(
            P("A", "2026-10-01", 100), P("A", "2026-10-02", 110), P("A", "2026-10-05", 121), P("A", "2026-10-06", 90.75));

        var result = await Service(prices).GetCompareChartDataAsync(new() { "A" }, days: 1);

        Assert.Equal(new[] { "2026-10-01", "2026-10-02", "2026-10-05", "2026-10-06" }, result.Timeline);
        Assert.Equal(new double?[] { null, 10, 10, -25 }, Assert.Single(result.SeriesData).Data);
    }

    [Fact]
    public async Task 報酬率四捨五入到小數兩位()
    {
        var prices = new FakePriceRepository(P("A", "2026-10-01", 3), P("A", "2026-10-02", 4));

        var result = await Service(prices).GetCompareChartDataAsync(new() { "A" }, days: 1);

        Assert.Equal(33.33, result.SeriesData[0].Data[1]);
    }

    [Fact]
    public async Task 時間軸為所有股票日期聯集_缺資料的日子為Null()
    {
        // B 在 10/02 停牌：時間軸仍包含 10/02，B 當天與「以 10/02 為基準」的那天都是 null
        var prices = new FakePriceRepository(
            P("A", "2026-10-01", 100), P("A", "2026-10-02", 101), P("A", "2026-10-05", 102),
            P("B", "2026-10-01", 50), P("B", "2026-10-05", 55));

        var result = await Service(prices).GetCompareChartDataAsync(new() { "A", "B" }, days: 1);

        Assert.Equal(3, result.Timeline.Count);
        var b = result.SeriesData.Single(s => s.Name == "B");
        Assert.Equal(new double?[] { null, null, null }, b.Data);
    }

    [Fact]
    public async Task 基準日收盤為零或缺值時為Null()
    {
        var prices = new FakePriceRepository(P("A", "2026-10-01", 0), P("A", "2026-10-02", 10), P("A", "2026-10-05", null));

        var result = await Service(prices).GetCompareChartDataAsync(new() { "A" }, days: 1);

        Assert.Equal(new double?[] { null, null, null }, result.SeriesData[0].Data);
    }

    [Fact]
    public async Task 圖例顯示代號與名稱_查無名稱只顯示代號()
    {
        var prices = new FakePriceRepository(P("0050", "2026-10-01", 1), P("9999", "2026-10-01", 1));
        var names = new FakeNameRepository(new() { ["0050"] = "元大台灣50" });

        var result = await Service(prices, names).GetCompareChartDataAsync(new() { "0050", "9999" }, days: 1);

        Assert.Equal(new[] { "0050 元大台灣50", "9999" }, result.SeriesData.Select(s => s.Name));
    }

    [Fact]
    public async Task 名稱查詢失敗時圖表仍然產生()
    {
        var prices = new FakePriceRepository(P("2330", "2026-10-01", 100), P("2330", "2026-10-02", 110));

        var result = await Service(prices, new FakeNameRepository(null, fail: true)).GetCompareChartDataAsync(new() { "2330" }, days: 1);

        var series = Assert.Single(result.SeriesData);
        Assert.Equal("2330", series.Name);
        Assert.Equal(10, series.Data[1]);
    }

    [Fact]
    public async Task 查無資料回傳空結果()
    {
        var result = await Service(new FakePriceRepository()).GetCompareChartDataAsync(new() { "A" }, days: 5);

        Assert.Empty(result.Timeline);
        Assert.Empty(result.SeriesData);
    }
}
