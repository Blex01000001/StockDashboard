using Microsoft.Extensions.Logging.Abstractions;
using StockDashboard.Models;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Implements;
using StockDashboard.Repositories.Interfaces;
using StockDashboard.Services;

namespace StockDashboard.Tests;

public class IndustryRepositoryTests : IDisposable
{
    private readonly TempSqliteDb _db = new();
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task 依產業別查股票代號_忽略前導0與空白_StockList不被修改()
    {
        _db.Execute("""
            CREATE TABLE StockList ("公司代號" TEXT, "公司簡稱" TEXT, "產 業 別" TEXT);
            INSERT INTO StockList VALUES ('2330', '台積電', '24');
            INSERT INTO StockList VALUES ('2303', '聯電', ' 24 ');
            INSERT INTO StockList VALUES ('1101', '台泥', '01');
            INSERT INTO StockList VALUES ('2317', '鴻海', '31');
            """);
        await DatabaseInitializer.InitializeAsync(_db.Configuration, NullLogger.Instance);
        var repo = new StockNameRepository(_db.Configuration);

        Assert.Equal(new[] { "2303", "2330" }, await repo.GetStockIdsByIndustryAsync("24"));
        Assert.Equal(new[] { "1101" }, await repo.GetStockIdsByIndustryAsync("1"));
        Assert.Equal(new[] { "1101" }, await repo.GetStockIdsByIndustryAsync("01"));
        Assert.Empty(await repo.GetStockIdsByIndustryAsync("99"));
        Assert.Empty(await repo.GetStockIdsByIndustryAsync(""));
    }

    [Fact]
    public async Task 產業別選單_代碼統一兩位數_附名稱與檔數_依代碼排序()
    {
        _db.Execute("""
            CREATE TABLE StockList ("公司代號" TEXT, "公司簡稱" TEXT, "產業別" TEXT);
            INSERT INTO StockList VALUES ('2330', '台積電', '24');
            INSERT INTO StockList VALUES ('2303', '聯電', '24');
            INSERT INTO StockList VALUES ('1101', '台泥', '1');
            INSERT INTO StockList VALUES ('1102', '亞泥', '01');
            INSERT INTO StockList VALUES ('9999', '怪怪', '77');
            INSERT INTO StockList VALUES ('8888', '沒有產業', NULL);
            """);
        await DatabaseInitializer.InitializeAsync(_db.Configuration, NullLogger.Instance);

        var options = await new StockNameRepository(_db.Configuration).GetIndustriesAsync();

        Assert.Equal(new[] { "01", "24", "77" }, options.Select(o => o.Code));
        Assert.Equal(new IndustryOption("01", "水泥工業", 2), options[0]);
        Assert.Equal("24 半導體業（2 檔）", options[1].Label);
        Assert.Equal("其他", options[2].Name);   // 不認得的代碼
    }

    [Fact]
    public async Task 沒有StockList或沒有產業別欄位_回傳空清單()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, NullLogger.Instance);
        var repo = new StockNameRepository(_db.Configuration);
        Assert.Empty(await repo.GetStockIdsByIndustryAsync("24"));
        Assert.Empty(await repo.GetIndustriesAsync());

        _db.Execute("""CREATE TABLE StockList ("公司代號" TEXT, "公司簡稱" TEXT); INSERT INTO StockList VALUES ('2330', '台積電');""");
        Assert.Empty(await repo.GetStockIdsByIndustryAsync("24"));
    }

    [Fact]
    public async Task 日線涵蓋記錄_各月最後交易日與追溯到上市日()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, NullLogger.Instance);
        var repo = new StockPriceRepository(_db.Configuration);
        await repo.UpsertPricesAsync(new[]
        {
            new StockDailyPriceEntity { StockId = "2330", TradeDate = "2026-09-01", ClosePrice = 1 },
            new StockDailyPriceEntity { StockId = "2330", TradeDate = "2026-09-30", ClosePrice = 1 },
            new StockDailyPriceEntity { StockId = "2330", TradeDate = "2026-10-15", ClosePrice = 1 },
            new StockDailyPriceEntity { StockId = "2317", TradeDate = "2026-10-30", ClosePrice = 1 },
        });

        var months = await repo.GetMonthLastDatesAsync("2330");
        Assert.Equal(new Dictionary<string, string> { ["2026-09"] = "2026-09-30", ["2026-10"] = "2026-10-15" }, months);

        Assert.False(await repo.IsHistoryReachedAsync("2330"));
        await repo.SetHistoryReachedAsync("2330");
        await repo.SetHistoryReachedAsync("2330");   // 重複寫入不出錯
        Assert.True(await repo.IsHistoryReachedAsync("2330"));
        Assert.False(await repo.IsHistoryReachedAsync("2317"));
    }
}

public class IndustryPriceSyncServiceTests : IDisposable
{
    private readonly TempSqliteDb _db = new();
    public void Dispose() => _db.Dispose();

    private static readonly DateTime Today = new(2026, 11, 8);
    private static DateTime M(int year, int month) => new(year, month, 1);

    /// <summary>假證交所：每檔股票有資料的月份；指定的股票月份拋錯</summary>
    private sealed class FakeTwse : ITwseApiClient
    {
        public Dictionary<string, HashSet<DateTime>> MonthsWithData { get; } = new();
        public (string StockId, DateTime Month)? FailOn { get; set; }
        public Action<string, DateTime>? OnCall { get; set; }
        public List<(string StockId, DateTime Month)> Calls { get; } = new();

        public Task<List<StockDailyPriceEntity>> GetMonthlyPricesAsync(string stockId, DateTime month, CancellationToken ct = default)
        {
            Calls.Add((stockId, month));
            OnCall?.Invoke(stockId, month);
            if (FailOn == (stockId, month)) throw new HttpRequestException("證交所回應 HTTP 403 Forbidden");
            // 每個有資料的月份給月初與 28 號兩筆 (28 號 ≥ 25 → 之後算完整月份)
            var list = MonthsWithData.TryGetValue(stockId, out var set) && set.Contains(month)
                ? new List<StockDailyPriceEntity>
                {
                    new() { StockId = stockId, TradeDate = month.ToString("yyyy-MM-02"), ClosePrice = 10 },
                    new() { StockId = stockId, TradeDate = month.ToString("yyyy-MM-28"), ClosePrice = 11 },
                }
                : new List<StockDailyPriceEntity>();
            return Task.FromResult(list);
        }

        public Task<List<InstitutionalDailyEntity>> GetInstitutionalTradingAsync(DateTime date, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<InstitutionalDailyEntity>> GetForeignHoldingAsync(DateTime date, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<StockNameEntity>> GetListedStocksAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeIndustry(params string[] ids) : IIndustryRepository
    {
        public Task<List<IndustryOption>> GetIndustriesAsync() => Task.FromResult(new List<IndustryOption>());

        public Task<List<string>> GetStockIdsByIndustryAsync(string industryCode) =>
            Task.FromResult(industryCode.Trim() == "24" ? ids.ToList() : new List<string>());
    }

    private async Task<(IndustryPriceSyncService Service, FakeTwse Twse, StockPriceRepository Repo)> InitAsync(params string[] ids)
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, NullLogger.Instance);
        var twse = new FakeTwse();
        var repo = new StockPriceRepository(_db.Configuration);
        return (new IndustryPriceSyncService(twse, repo, repo, new FakeIndustry(ids), 0, () => Today), twse, repo);
    }

    [Fact]
    public void 月份是否完整_早於本月且最後交易日在25號之後()
    {
        var thisMonth = M(2026, 11);
        var dates = new Dictionary<string, string> { ["2026-10"] = "2026-10-30", ["2026-09"] = "2026-09-24", ["2026-11"] = "2026-11-28" };
        Assert.True(IndustryPriceSyncService.IsMonthComplete(M(2026, 10), thisMonth, dates));
        Assert.False(IndustryPriceSyncService.IsMonthComplete(M(2026, 9), thisMonth, dates));    // 最後交易日 24 號：當時可能只抓到一半
        Assert.False(IndustryPriceSyncService.IsMonthComplete(M(2026, 11), thisMonth, dates));   // 本月一律重抓
        Assert.False(IndustryPriceSyncService.IsMonthComplete(M(2026, 8), thisMonth, dates));    // 沒資料
    }

    [Fact]
    public async Task 找不到產業別_回報錯誤且不發請求()
    {
        var (service, twse, _) = await InitAsync("2330");
        var result = await service.SyncAsync("99", null, CancellationToken.None);
        Assert.Contains("99", result.Error);
        Assert.Empty(twse.Calls);
    }

    [Fact]
    public async Task 每檔往前抓到上市日_連續三個月無資料就停_並記下已追溯到上市日()
    {
        var (service, twse, repo) = await InitAsync("2330", "2303");
        twse.MonthsWithData["2330"] = new() { M(2026, 11), M(2026, 10), M(2026, 9) };
        twse.MonthsWithData["2303"] = new() { M(2026, 10) };

        var reports = new List<IndustryPriceSyncProgress>();
        var result = await service.SyncAsync("24", new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal((2, 2), (result.StocksDone, result.StocksTotal));
        Assert.Equal(6 + 5, result.Requests);   // 2330：11~6 月 6 次；2303：本月(不計)、10 月、9/8/7 月 5 次
        Assert.Equal(3 * 2 + 1 * 2, result.RowsWritten);
        Assert.True(await repo.IsHistoryReachedAsync("2330"));
        Assert.True(await repo.IsHistoryReachedAsync("2303"));
        Assert.Contains(reports, r => r.StockId == "2303" && r.StockIndex == 2 && r.StockTotal == 2);
    }

    [Fact]
    public async Task 續跑_已完整的月份不重抓_已追溯到上市日不再往前查()
    {
        var (service, twse, _) = await InitAsync("2330");
        twse.MonthsWithData["2330"] = new() { M(2026, 11), M(2026, 10), M(2026, 9) };
        await service.SyncAsync("24", null, CancellationToken.None);

        twse.Calls.Clear();
        var second = await service.SyncAsync("24", null, CancellationToken.None);

        Assert.Equal(new[] { ("2330", M(2026, 11)) }, twse.Calls);   // 只重抓本月
        Assert.Equal(1, second.Requests);
        Assert.Equal(1, second.StocksDone);
    }

    [Fact]
    public async Task 被封鎖_立即停止整批_不當成沒資料_已抓到的仍寫入()
    {
        var (service, twse, repo) = await InitAsync("2330", "2303", "2454");
        twse.MonthsWithData["2330"] = new() { M(2026, 11), M(2026, 10) };
        twse.MonthsWithData["2303"] = new() { M(2026, 11), M(2026, 10), M(2026, 9) };
        twse.FailOn = ("2303", M(2026, 9));

        var result = await service.SyncAsync("24", null, CancellationToken.None);

        Assert.Contains("2303", result.Error);
        Assert.Contains("403", result.Error);
        Assert.Equal(1, result.StocksDone);                          // 只有 2330 完成
        Assert.DoesNotContain(twse.Calls, c => c.StockId == "2454"); // 後面的股票沒有再請求
        Assert.False(await repo.IsHistoryReachedAsync("2303"));      // 被封鎖的不能被誤記成已到上市日
        Assert.Equal(2, (await repo.GetMonthLastDatesAsync("2303")).Count);   // 封鎖前抓到的 11、10 月已寫入
    }

    [Fact]
    public async Task 取消_停止請求_已抓到的寫入()
    {
        var (service, twse, repo) = await InitAsync("2330", "2303");
        twse.MonthsWithData["2330"] = new() { M(2026, 11), M(2026, 10), M(2026, 9) };
        using var cts = new CancellationTokenSource();
        twse.OnCall = (_, m) => { if (m == M(2026, 10)) cts.Cancel(); };

        var result = await service.SyncAsync("24", null, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(2, twse.Calls.Count);
        Assert.Equal(0, result.StocksDone);
        Assert.Equal(2, (await repo.GetMonthLastDatesAsync("2330")).Count);
        Assert.False(await repo.IsHistoryReachedAsync("2330"));
    }

    private sealed class SyncProgress(Action<IndustryPriceSyncProgress> action) : IProgress<IndustryPriceSyncProgress>
    {
        public void Report(IndustryPriceSyncProgress value) => action(value);
    }
}
