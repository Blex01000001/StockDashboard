using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Implements;

namespace StockDashboard.Tests;

public class StockNameRepositoryTests : IDisposable
{
    private readonly TempSqliteDb _db = new();

    public void Dispose() => _db.Dispose();

    // 模擬使用者既有的「上市公司基本資料」表 (只列出部分中文欄位)
    private void CreateUserStockList(string shortNameColumn = "公司簡稱")
    {
        _db.Execute($"""
            CREATE TABLE StockList ("出表日期" TEXT, "公司代號" TEXT, "公司名稱" TEXT, "{shortNameColumn}" TEXT, "產業別" TEXT);
            INSERT INTO StockList VALUES ('1150120', '1101', '臺灣水泥股份有限公司', '台泥', '1');
            INSERT INTO StockList VALUES ('1150120', '1102', '亞洲水泥股份有限公司', '亞泥', '1');
            """);
    }

    private string DumpStockList()
    {
        using var c = new SqliteConnection(_db.ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'StockList'; ";
        string schema = (string)cmd.ExecuteScalar()!;
        cmd.CommandText = "SELECT * FROM StockList";
        using var r = cmd.ExecuteReader();
        var values = new List<object>();
        while (r.Read()) for (int i = 0; i < r.FieldCount; i++) values.Add(r.GetValue(i));
        return schema + "|" + string.Join("|", values);
    }

    private async Task<StockNameRepository> InitAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, NullLogger.Instance);
        return new StockNameRepository(_db.Configuration);
    }

    private static StockNameEntity N(string id, string name) => new() { StockId = id, StockName = name, UpdatedAt = "2026-11-01 10:00:00" };

    [Theory]
    [InlineData("公司簡稱")]
    [InlineData("公司 簡稱")]   // 欄位名稱含空白也要能讀到
    public async Task 讀取使用者StockList的公司簡稱(string shortNameColumn)
    {
        CreateUserStockList(shortNameColumn);
        var repo = await InitAsync();

        var names = await repo.GetStockNamesAsync(new[] { "1101", "1102", "2330" });

        Assert.Equal("台泥", names["1101"]);
        Assert.Equal("亞泥", names["1102"]);
        Assert.False(names.ContainsKey("2330"));
    }

    [Fact]
    public async Task 合併兩個來源_同代號以StockList為準()
    {
        CreateUserStockList();
        var repo = await InitAsync();
        await repo.UpsertStocksAsync(new[] { N("0050", "元大台灣50"), N("1101", "同步清單的名稱") });

        Assert.Equal("台泥", await repo.GetStockNameAsync("1101"));
        Assert.Equal("元大台灣50", await repo.GetStockNameAsync("0050"));
        Assert.Null(await repo.GetStockNameAsync("9999"));
        Assert.Equal(new[] { "0050", "1101", "1102" }, (await repo.GetAllStocksAsync()).Select(s => s.StockId));
    }

    [Fact]
    public async Task 絕不修改使用者的StockList()
    {
        CreateUserStockList();
        string before = DumpStockList();
        var repo = await InitAsync();

        await repo.UpsertStocksAsync(new[] { N("1101", "改名測試"), N("2330", "台積電") });
        await repo.GetAllStocksAsync();
        await repo.GetSummaryAsync();

        Assert.Equal(before, DumpStockList());
    }

    [Fact]
    public async Task 統計兩個來源的筆數()
    {
        CreateUserStockList();
        var repo = await InitAsync();
        await repo.UpsertStocksAsync(new[] { N("0050", "元大台灣50") });

        var summary = await repo.GetSummaryAsync();

        Assert.Equal(2, summary.CompanyCount);
        Assert.Equal(1, summary.SyncedCount);
        Assert.Equal("2026-11-01 10:00:00", summary.LastSyncedAt);
    }

    [Fact]
    public async Task 沒有StockList時只使用同步清單()
    {
        var repo = await InitAsync();
        await repo.UpsertStocksAsync(new[] { N("0050", "元大台灣50") });

        Assert.Equal("元大台灣50", await repo.GetStockNameAsync("0050"));
        Assert.Null((await repo.GetSummaryAsync()).CompanyCount);
    }

    [Fact]
    public async Task 重複同步只更新不重複新增()
    {
        var repo = await InitAsync();
        await repo.UpsertStocksAsync(new[] { N("0050", "舊名稱") });
        await repo.UpsertStocksAsync(new[] { N("0050", "元大台灣50") });

        Assert.Equal(1, (await repo.GetSummaryAsync()).SyncedCount);
        Assert.Equal("元大台灣50", await repo.GetStockNameAsync("0050"));
    }
}

public class StockPriceRepositoryTests : IDisposable
{
    private readonly TempSqliteDb _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<StockPriceRepository> InitAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, NullLogger.Instance);
        return new StockPriceRepository(_db.Configuration);
    }

    private static StockDailyPriceEntity P(string date, double close) =>
        new() { StockId = "2330", TradeDate = date, OpenPrice = close, HighPrice = close, LowPrice = close, ClosePrice = close, Volume = 1 };

    [Fact]
    public async Task 寫入後覆寫同一天的資料()
    {
        var repo = await InitAsync();
        await repo.UpsertPricesAsync(new[] { P("2026-10-01", 100), P("2026-10-02", 101) });
        await repo.UpsertPricesAsync(new[] { P("2026-10-02", 999) });

        var latest = await repo.GetLatestPriceAsync("2330");

        Assert.Equal("2026-10-02", latest!.TradeDate);
        Assert.Equal(999, latest.ClosePrice);
    }

    [Fact]
    public async Task K線取最新N筆且由舊到新排序()
    {
        var repo = await InitAsync();
        await repo.UpsertPricesAsync(new[] { P("2026-10-01", 1), P("2026-10-02", 2), P("2026-10-05", 3), P("2026-10-06", 4) });

        var candles = (await repo.GetLatestCandlesAsync("2330", 3)).ToList();

        Assert.Equal(new[] { "2026-10-02", "2026-10-05", "2026-10-06" }, candles.Select(c => c.Date));
        Assert.Equal(4, candles[^1].Close);
    }

    [Fact]
    public async Task 查無資料回傳Null()
    {
        var repo = await InitAsync();

        Assert.Null(await repo.GetLatestPriceAsync("9999"));
    }
}
