using StockDashboard.Models.Entities;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Implements;
using StockDashboard.Services;

namespace StockDashboard.Tests;

public class PriceAdjusterTests
{
    private static StockDailyPriceEntity Bar(string date, double close) => new()
    {
        StockId = "2330", TradeDate = date, OpenPrice = close, HighPrice = close + 2, LowPrice = close - 2,
        ClosePrice = close, PriceChange = 1, Volume = 1000, Amount = 5000, TradeCount = 7, Note = "n",
    };

    private static StockCorporateActionEntity Action(string exDate, double preClose, double refPrice) =>
        new() { StockId = "2330", ExDate = exDate, PreClose = preClose, RefPrice = refPrice };

    [Fact]
    public void 單次除息_之前乘0點98_當天與之後不變_量不變()
    {
        // 前收盤 100、配息 2 元 → 參考價 98 → 係數 0.98 (手算)
        var prices = new[] { Bar("2026-01-02", 100), Bar("2026-01-05", 98), Bar("2026-01-06", 99) };
        var result = PriceAdjuster.Apply(prices, new[] { Action("2026-01-05", 100, 98) });

        Assert.Equal(98.0, result[0].ClosePrice);     // 100 × 0.98
        Assert.Equal(99.96, result[0].HighPrice);     // 最高價 102 × 0.98
        Assert.Equal(98.0, result[1].ClosePrice);     // 除權息當天不調整
        Assert.Equal(99.0, result[2].ClosePrice);
        Assert.Equal((1000L, 5000L, 7, "n"), (result[0].Volume, result[0].Amount, result[0].TradeCount, result[0].Note));
    }

    [Fact]
    public void 多次事件_係數累乘_更早的價格乘兩次()
    {
        // 事件 A (01-10)：100→90 係數 0.9；事件 B (01-20)：50→40 係數 0.8
        var prices = new[] { Bar("2026-01-05", 100), Bar("2026-01-15", 100), Bar("2026-01-25", 100) };
        var actions = new[] { Action("2026-01-20", 50, 40), Action("2026-01-10", 100, 90) };   // 故意亂序
        var result = PriceAdjuster.Apply(prices, actions);

        Assert.Equal(72.0, result[0].ClosePrice);     // 100 × 0.9 × 0.8
        Assert.Equal(80.0, result[1].ClosePrice);     // 只受 B 影響
        Assert.Equal(100.0, result[2].ClosePrice);    // 最新價 = 原始價
    }

    [Fact]
    public void 沒有事件_或壞資料_價格原樣且不改輸入物件()
    {
        var prices = new[] { Bar("2026-01-02", 100) };
        var bad = new[] { Action("2026-01-05", 0, 98), Action("2026-01-06", 100, 0), Action("", 100, 90) };

        Assert.Equal(100.0, PriceAdjuster.Apply(prices, Array.Empty<StockCorporateActionEntity>())[0].ClosePrice);
        Assert.Equal(100.0, PriceAdjuster.Apply(prices, bad)[0].ClosePrice);

        PriceAdjuster.Apply(prices, new[] { Action("2026-01-05", 100, 50) });
        Assert.Equal(100.0, prices[0].ClosePrice);    // 輸入沒被改
    }

    [Fact]
    public void 價格為null保持null_並四捨五入到2位()
    {
        var p = Bar("2026-01-02", 100);
        p.OpenPrice = null;
        p.ClosePrice = 33.33;
        var r = PriceAdjuster.Apply(new[] { p }, new[] { Action("2026-01-05", 3, 1) })[0];   // 係數 1/3

        Assert.Null(r.OpenPrice);
        Assert.Equal(11.11, r.ClosePrice);            // 33.33 / 3 = 11.11
    }

    [Fact]
    public void 事件日不是交易日_仍以日期比較套用()
    {
        var result = PriceAdjuster.Apply(
            new[] { Bar("2026-01-02", 100), Bar("2026-01-07", 100) },
            new[] { Action("2026-01-04", 100, 90) });   // 01-04 為週日
        Assert.Equal((90.0, 100.0), (result[0].ClosePrice, result[1].ClosePrice));
    }
}

public class StockCorporateActionRepositoryTests : IDisposable
{
    private readonly TempSqliteDb _db = new();
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task 寫入_同日覆蓋_讀回由舊到新_只含該股票()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var repo = new StockCorporateActionRepository(_db.Configuration);

        await repo.UpsertAsync(new[]
        {
            new StockCorporateActionEntity { StockId = "2330", ExDate = "2026-06-10", PreClose = 100, RefPrice = 98 },
            new StockCorporateActionEntity { StockId = "2330", ExDate = "2025-06-10", PreClose = 80, RefPrice = 79 },
            new StockCorporateActionEntity { StockId = "2317", ExDate = "2026-06-10", PreClose = 50, RefPrice = 49 },
        });
        await repo.UpsertAsync(new[] { new StockCorporateActionEntity { StockId = "2330", ExDate = "2026-06-10", PreClose = 100, RefPrice = 97 } });

        var rows = (await repo.GetByStockIdAsync("2330")).ToList();
        Assert.Equal(new[] { "2025-06-10", "2026-06-10" }, rows.Select(r => r.ExDate));
        Assert.Equal(97.0, rows[1].RefPrice);
        Assert.Empty(await repo.GetByStockIdAsync("9999"));
    }

    [Fact]
    public async Task 服務_還原開關_只在isAdjusted時套用()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var priceRepo = new StockPriceRepository(_db.Configuration);
        await priceRepo.UpsertPricesAsync(new[]
        {
            new StockDailyPriceEntity { StockId = "2330", TradeDate = "2026-01-02", ClosePrice = 100 },
            new StockDailyPriceEntity { StockId = "2330", TradeDate = "2026-01-05", ClosePrice = 98 },
        });
        var actionRepo = new StockCorporateActionRepository(_db.Configuration);
        await actionRepo.UpsertAsync(new[] { new StockCorporateActionEntity { StockId = "2330", ExDate = "2026-01-05", PreClose = 100, RefPrice = 98 } });
        var service = new StockPriceService(priceRepo, actionRepo);

        var raw = (await service.GetPricesAsync("2330", "2026-01-01", "2026-01-31", false)).ToList();
        var adjusted = (await service.GetPricesAsync("2330", "2026-01-01", "2026-01-31", true)).ToList();

        Assert.Equal(100.0, raw[0].ClosePrice);
        Assert.Equal(98.0, adjusted[0].ClosePrice);
        Assert.Equal(98.0, adjusted[1].ClosePrice);
    }
}

public class StockCorporateActionSyncTests : IDisposable
{
    private readonly TempSqliteDb _db = new();
    public void Dispose() => _db.Dispose();

    private sealed class FakeApi(List<StockCorporateActionEntity>? rows = null, Exception? error = null) : IStockCorporateActionApiClient
    {
        public Task<List<StockCorporateActionEntity>> GetActionsAsync(string stockId, CancellationToken cancellationToken = default) =>
            error != null ? throw error : Task.FromResult(rows!);
    }

    private static StockCorporateActionEntity A(string exDate, double pre, double reference) =>
        new() { StockId = "ignored", ExDate = exDate, PreClose = pre, RefPrice = reference };

    private async Task<StockCorporateActionRepository> InitAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        return new StockCorporateActionRepository(_db.Configuration);
    }

    [Fact]
    public async Task 同步_寫入有效事件_略過壞資料_代號以要求的為準()
    {
        var repo = await InitAsync();
        var api = new FakeApi(new() { A("2026-06-10", 100, 98), A("2025-06-10", 80, 79), A("2026-01-01", 0, 5), A("", 10, 9) });

        var result = await new StockCorporateActionSyncService(api, repo).SyncAsync("2330", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal((2, "2025-06-10", "2026-06-10"), (result.RowsWritten, result.FirstDate, result.LastDate));
        Assert.Equal(2, (await repo.GetByStockIdAsync("2330")).Count());
    }

    [Fact]
    public async Task 同步_沒有事件或來源出錯_回報錯誤且不寫入()
    {
        var repo = await InitAsync();

        var empty = await new StockCorporateActionSyncService(new FakeApi(new()), repo).SyncAsync("2330", CancellationToken.None);
        var failed = await new StockCorporateActionSyncService(new FakeApi(error: new InvalidOperationException("HTTP 403")), repo).SyncAsync("2330", CancellationToken.None);

        Assert.Contains("2330", empty.Error);
        Assert.Equal("HTTP 403", failed.Error);
        Assert.Empty(await repo.GetByStockIdAsync("2330"));
    }

    [Fact]
    public async Task 同步_取消時標記Cancelled()
    {
        var repo = await InitAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await new StockCorporateActionSyncService(new FakeApi(error: new OperationCanceledException(cts.Token)), repo).SyncAsync("2330", cts.Token);
        Assert.True(result.Cancelled);
    }
}
