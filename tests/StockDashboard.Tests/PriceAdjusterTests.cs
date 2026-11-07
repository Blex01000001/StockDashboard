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
    public async Task 使用者資料庫已有同名但欄位不同的StockCorporateAction表_不受影響()
    {
        // 實際發生過：使用者的 stock.db 已有 StockCorporateAction (欄位不同)，本專案的表改名為 StockExRightEvent 以避開
        _db.Execute("""
            CREATE TABLE "StockCorporateAction" ("StockId" TEXT NOT NULL, "ActionType" TEXT NOT NULL, "ExDate" TEXT NOT NULL, "Ratio" REAL, "CashAmount" INTEGER,
                "Description" TEXT, "CreatedAt" INTEGER DEFAULT CURRENT_TIMESTAMP, PRIMARY KEY ("StockId","ActionType","ExDate"));
            INSERT INTO StockCorporateAction (StockId, ActionType, ExDate, Description) VALUES ('9933', 'dividend', '2026-01-05', 'mine');
            """);
        await DatabaseInitializer.InitializeAsync(_db.Configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var repo = new StockCorporateActionRepository(_db.Configuration);

        await repo.UpsertAsync(new[] { new StockCorporateActionEntity { StockId = "9933", ExDate = "2026-01-05", PreClose = 100, RefPrice = 98 } });

        Assert.Equal(98.0, (await repo.GetByStockIdAsync("9933")).Single().RefPrice);
        using var c = new Microsoft.Data.Sqlite.SqliteConnection(_db.ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Description FROM StockCorporateAction";
        Assert.Equal("mine", cmd.ExecuteScalar());   // 使用者的表原封不動
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

    // 依代號回傳事件；可指定某代號丟例外
    private sealed class FakeApi(Dictionary<string, List<StockCorporateActionEntity>> rows, Dictionary<string, Exception>? errors = null) : IStockCorporateActionApiClient
    {
        public List<string> Requested { get; } = new();
        public Task<List<StockCorporateActionEntity>> GetActionsAsync(string stockId, CancellationToken cancellationToken = default)
        {
            Requested.Add(stockId);
            if (errors != null && errors.TryGetValue(stockId, out var ex)) throw ex;
            return Task.FromResult(rows.TryGetValue(stockId, out var l) ? l : new List<StockCorporateActionEntity>());
        }
    }

    private static StockCorporateActionEntity A(string exDate, double pre, double reference) =>
        new() { StockId = "ignored", ExDate = exDate, PreClose = pre, RefPrice = reference };

    private async Task<StockCorporateActionRepository> InitAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        return new StockCorporateActionRepository(_db.Configuration);
    }

    [Fact]
    public async Task 同步_逐檔寫入有效事件_略過壞資料_沒有事件的不算錯誤()
    {
        var repo = await InitAsync();
        var api = new FakeApi(new()
        {
            ["2330"] = new() { A("2026-06-10", 100, 98), A("2025-06-10", 80, 79), A("2026-01-01", 0, 5), A("", 10, 9) },
            ["0050"] = new() { A("2026-01-17", 131.65, 128.65) },
        });

        var result = await new StockCorporateActionSyncService(api, repo, 0).SyncAsync(new[] { "2330", "9999", "0050" }, null, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal((3, 1, 3, 3), (result.StocksDone, result.StocksWithoutEvents, result.RowsWritten, result.Requests));
        Assert.Equal(2, (await repo.GetByStockIdAsync("2330")).Count());   // 壞資料沒寫入
        Assert.Equal(128.65, (await repo.GetByStockIdAsync("0050")).Single().RefPrice);
        Assert.Equal((2, 3), await repo.GetSummaryAsync());
    }

    [Fact]
    public async Task 同步_封鎖時立即停止_已完成的保留()
    {
        var repo = await InitAsync();
        var api = new FakeApi(new() { ["A"] = new() { A("2026-01-02", 10, 9) } }, new() { ["B"] = new FinMindBlockedException("ip banned") });

        var result = await new StockCorporateActionSyncService(api, repo, 0).SyncAsync(new[] { "A", "B", "C" }, null, CancellationToken.None);

        Assert.True(result.Blocked);
        Assert.Equal("ip banned", result.Error);
        Assert.Equal(1, result.StocksDone);
        Assert.Equal(new[] { "A", "B" }, api.Requested);                    // C 不再請求
        Assert.Single(await repo.GetByStockIdAsync("A"));
    }

    [Fact]
    public async Task 同步_其他錯誤停止並標示代號_不算封鎖()
    {
        var repo = await InitAsync();
        var api = new FakeApi(new(), new() { ["A"] = new InvalidOperationException("HTTP 500") });

        var result = await new StockCorporateActionSyncService(api, repo, 0).SyncAsync(new[] { "A", "B" }, null, CancellationToken.None);

        Assert.False(result.Blocked);
        Assert.Equal("A：HTTP 500", result.Error);
        Assert.Equal(new[] { "A" }, api.Requested);
    }

    [Fact]
    public async Task 同步_取消時標記Cancelled_並回報進度()
    {
        var repo = await InitAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var api = new FakeApi(new());
        var result = await new StockCorporateActionSyncService(api, repo, 0).SyncAsync(new[] { "A" }, null, cts.Token);
        Assert.True(result.Cancelled);
        Assert.Empty(api.Requested);

        var reports = new List<StockCorporateActionSyncProgress>();
        await new StockCorporateActionSyncService(new FakeApi(new()), repo, 0).SyncAsync(new[] { "A" }, new Progress<StockCorporateActionSyncProgress>(reports.Add), CancellationToken.None);
        await Task.Delay(50);   // Progress<T> 以同步內容回報，稍等讓回呼完成
        Assert.Contains(reports, r => r.Done == 1 && r.Total == 1);
    }
}

public class FinMindCorporateActionApiTests
{
    [Fact]
    public async Task 解析FinMind除權除息_欄位對應_由舊到新()
    {
        var handler = FakeHttpHandler.Always(Fixtures.Read("finmind_dividend_result_2330.json"));
        var client = new FinMindCorporateActionApiClient(new HttpClient(handler), null, () => new DateTime(2026, 11, 7));

        var rows = await client.GetActionsAsync("2330");

        Assert.Equal(new[] { "2020-03-19", "2020-06-18", "2026-09-16" }, rows.Select(r => r.ExDate));
        Assert.All(rows, r => Assert.Equal("2330", r.StockId));
        Assert.Equal((260.0, 257.5), (rows[0].PreClose, rows[0].RefPrice));
        Assert.Equal((2385.0, 2377.99), (rows[2].PreClose, rows[2].RefPrice));   // 用參考價，不是開盤價 2380

        var url = handler.Requests.Single().ToString();
        Assert.Contains("dataset=TaiwanStockDividendResult", url);
        Assert.Contains("data_id=2330", url);
        Assert.Contains("start_date=2003-01-01", url);
        Assert.Contains("end_date=2026-11-07", url);
    }

    [Fact]
    public void 解析_缺參考價用after_price_缺前收盤或日期的略過_同日以最後一筆為準()
    {
        var json = """
            {"msg":"success","status":200,"data":[
              {"date":"2026-01-02","before_price":100,"after_price":98},
              {"date":"2026-01-03","after_price":98},
              {"date":"2026-01-04","before_price":0,"reference_price":98},
              {"before_price":100,"reference_price":98},
              {"date":"2026-01-05","before_price":"50.5","reference_price":"49.5"},
              {"date":"2026-01-05","before_price":60,"reference_price":59}
            ]}
            """;
        var rows = FinMindCorporateActionApiClient.Parse("1101", FinMindApiClient.ParseResponse(json, 200));

        Assert.Equal(new[] { "2026-01-02", "2026-01-05" }, rows.Select(r => r.ExDate));
        Assert.Equal(98.0, rows[0].RefPrice);                      // 沒有 reference_price → after_price
        Assert.Equal((60.0, 59.0), (rows[1].PreClose, rows[1].RefPrice));
    }

    [Fact]
    public async Task 封鎖時丟FinMindBlockedException()
    {
        var handler = FakeHttpHandler.Always(Fixtures.Read("finmind_ip_banned.json"), status: System.Net.HttpStatusCode.Forbidden);
        var client = new FinMindCorporateActionApiClient(new HttpClient(handler));
        await Assert.ThrowsAsync<FinMindBlockedException>(() => client.GetActionsAsync("2330"));
    }
}
