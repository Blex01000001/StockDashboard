using Microsoft.Extensions.Logging.Abstractions;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Implements;
using StockDashboard.Services;

namespace StockDashboard.Tests;

public class PriceSyncServiceTests : IDisposable
{
    private readonly TempSqliteDb _db = new();
    public void Dispose() => _db.Dispose();

    /// <summary>假證交所：指定月份有一筆資料，其餘為空；可指定某月拋錯或在某月取消</summary>
    private sealed class FakeTwse : ITwseApiClient
    {
        public HashSet<DateTime> MonthsWithData { get; } = new();
        public HashSet<DateTime> FailMonths { get; } = new();
        public Action<DateTime>? OnCall { get; set; }
        public List<DateTime> Calls { get; } = new();

        public Task<List<StockDailyPriceEntity>> GetMonthlyPricesAsync(string stockId, DateTime month, CancellationToken ct = default)
        {
            Calls.Add(month);
            OnCall?.Invoke(month);
            if (FailMonths.Contains(month)) throw new HttpRequestException("證交所回應 HTTP 403 Forbidden");
            var list = MonthsWithData.Contains(month)
                ? new List<StockDailyPriceEntity> { new() { StockId = stockId, TradeDate = month.ToString("yyyy-MM-15"), ClosePrice = 100 } }
                : new List<StockDailyPriceEntity>();
            return Task.FromResult(list);
        }

        public Task<List<InstitutionalDailyEntity>> GetInstitutionalTradingAsync(DateTime date, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<InstitutionalDailyEntity>> GetForeignHoldingAsync(DateTime date, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<StockNameEntity>> GetListedStocksAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static readonly DateTime Today = new(2026, 11, 8);
    private static DateTime M(int year, int month) => new(year, month, 1);

    private async Task<(PriceSyncService Service, FakeTwse Twse, StockPriceRepository Repo)> InitAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, NullLogger.Instance);
        var twse = new FakeTwse();
        var repo = new StockPriceRepository(_db.Configuration);
        return (new PriceSyncService(twse, repo, 0, () => Today), twse, repo);
    }

    [Fact]
    public async Task 逐月由本月往前_全部有資料_寫入資料庫()
    {
        var (service, twse, _) = await InitAsync();
        twse.MonthsWithData.UnionWith(new[] { M(2026, 11), M(2026, 10), M(2026, 9) });

        var reports = new List<PriceSyncProgress>();
        var result = await service.SyncAsync("2330", 3, new SyncProgress<PriceSyncProgress>(reports.Add), CancellationToken.None);

        Assert.Equal(new[] { M(2026, 11), M(2026, 10), M(2026, 9) }, twse.Calls);
        Assert.Equal(3, result.SuccessMonths);
        Assert.Equal(3, result.RowsFetched);
        Assert.Equal(3, result.RowsWritten);
        Assert.False(result.Broken);
        Assert.Null(result.LastError);
        Assert.Equal(new[] { 1, 2, 3 }, reports.Select(r => r.Index));
    }

    [Fact]
    public async Task 本月沒資料不計入熔斷()
    {
        var (service, twse, _) = await InitAsync();
        twse.MonthsWithData.UnionWith(new[] { M(2026, 10), M(2026, 9) });

        var result = await service.SyncAsync("2330", 3, null, CancellationToken.None);

        Assert.Equal(2, result.SuccessMonths);
        Assert.False(result.Broken);
    }

    [Fact]
    public async Task 連續三個月無資料提前中斷()
    {
        var (service, twse, _) = await InitAsync();
        twse.MonthsWithData.Add(M(2026, 10));   // 之前的月份都沒有 (例如尚未上市)

        var result = await service.SyncAsync("2330", 12, null, CancellationToken.None);

        Assert.True(result.Broken);
        // 本月(不計)、10 月(有)、9/8/7 月連續三次無資料後停止
        Assert.Equal(5, twse.Calls.Count);
        Assert.Equal(1, result.RowsWritten);
    }

    [Fact]
    public async Task 下載失敗計入熔斷並記錄最後錯誤()
    {
        var (service, twse, _) = await InitAsync();
        twse.FailMonths.UnionWith(new[] { M(2026, 10), M(2026, 9), M(2026, 8) });

        var result = await service.SyncAsync("2330", 6, null, CancellationToken.None);

        Assert.True(result.Broken);
        Assert.Equal(3, result.ErrorCount);
        Assert.Contains("2026年08月", result.LastError);
        Assert.Contains("403", result.LastError);
        Assert.Equal(0, result.RowsFetched);
        Assert.Equal(0, result.RowsWritten);
    }

    [Fact]
    public async Task 取消後不再請求_已抓到的資料仍寫入()
    {
        var (service, twse, _) = await InitAsync();
        twse.MonthsWithData.UnionWith(new[] { M(2026, 11), M(2026, 10), M(2026, 9) });
        using var cts = new CancellationTokenSource();
        twse.OnCall = m => { if (m == M(2026, 10)) cts.Cancel(); };

        var result = await service.SyncAsync("2330", 3, null, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(2, twse.Calls.Count);
        Assert.Equal(2, result.RowsWritten);
    }

    // 多檔用：同一個假證交所依代號決定哪些月份有資料
    private sealed class FakeTwseByStock : ITwseApiClient
    {
        public Dictionary<string, HashSet<DateTime>> MonthsWithData { get; } = new();
        public HashSet<string> FailStocks { get; } = new();
        public Action<string, DateTime>? OnCall { get; set; }
        public List<(string StockId, DateTime Month)> Calls { get; } = new();

        public Task<List<StockDailyPriceEntity>> GetMonthlyPricesAsync(string stockId, DateTime month, CancellationToken ct = default)
        {
            Calls.Add((stockId, month));
            OnCall?.Invoke(stockId, month);
            if (FailStocks.Contains(stockId)) throw new HttpRequestException("證交所回應 HTTP 403 Forbidden");
            var list = MonthsWithData.TryGetValue(stockId, out var set) && set.Contains(month)
                ? new List<StockDailyPriceEntity> { new() { StockId = stockId, TradeDate = month.ToString("yyyy-MM-15"), ClosePrice = 100 } }
                : new List<StockDailyPriceEntity>();
            return Task.FromResult(list);
        }

        public Task<List<InstitutionalDailyEntity>> GetInstitutionalTradingAsync(DateTime date, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<InstitutionalDailyEntity>> GetForeignHoldingAsync(DateTime date, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<StockNameEntity>> GetListedStocksAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private async Task<(PriceSyncService Service, FakeTwseByStock Twse, StockPriceRepository Repo)> InitManyAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, NullLogger.Instance);
        var twse = new FakeTwseByStock();
        var repo = new StockPriceRepository(_db.Configuration);
        return (new PriceSyncService(twse, repo, 0, () => Today), twse, repo);
    }

    [Fact]
    public async Task 多檔_依序同步每一檔_進度帶股票資訊()
    {
        var (service, twse, repo) = await InitManyAsync();
        twse.MonthsWithData["006208"] = new() { M(2026, 11), M(2026, 10) };
        twse.MonthsWithData["00981A"] = new() { M(2026, 11) };

        var reports = new List<PriceSyncProgress>();
        var multi = await service.SyncManyAsync(new[] { "006208", "00981A" }, 2, new SyncProgress<PriceSyncProgress>(reports.Add), CancellationToken.None);

        Assert.Equal(new[] { "006208", "006208", "00981A", "00981A" }, twse.Calls.Select(c => c.StockId));
        Assert.Equal(new[] { "006208", "00981A" }, multi.Stocks.Select(s => s.StockId));
        Assert.Equal(3, multi.RowsWritten);
        Assert.Null(multi.StoppedReason);
        Assert.False(multi.Cancelled);
        Assert.Contains(reports, r => r.StockId == "00981A" && r.StockIndex == 2 && r.StockTotal == 2);
        Assert.Equal(2, (await repo.GetMonthLastDatesAsync("006208")).Count);
    }

    [Fact]
    public async Task 多檔_某檔沒有資料但沒有錯誤_繼續下一檔()
    {
        var (service, twse, _) = await InitManyAsync();
        twse.MonthsWithData["00991A"] = new() { M(2026, 11) };   // 006208 完全沒資料 (例如代號不存在)

        var multi = await service.SyncManyAsync(new[] { "006208", "00991A" }, 1, null, CancellationToken.None);

        Assert.Equal(2, multi.Stocks.Count);
        Assert.Equal(0, multi.Stocks[0].Result.RowsFetched);
        Assert.Equal(1, multi.Stocks[1].Result.RowsWritten);
        Assert.Null(multi.StoppedReason);
    }

    [Fact]
    public async Task 多檔_某檔完全下載失敗_中斷整批_前面的已寫入()
    {
        var (service, twse, repo) = await InitManyAsync();
        twse.MonthsWithData["006208"] = new() { M(2026, 11) };
        twse.FailStocks.Add("00981A");

        var multi = await service.SyncManyAsync(new[] { "006208", "00981A", "00991A" }, 1, null, CancellationToken.None);

        Assert.Contains("00981A", multi.StoppedReason);
        Assert.Contains("403", multi.StoppedReason);
        Assert.Equal(2, multi.Stocks.Count);                              // 00991A 沒有被處理
        Assert.DoesNotContain(twse.Calls, c => c.StockId == "00991A");
        Assert.Single(await repo.GetMonthLastDatesAsync("006208"));
    }

    [Fact]
    public async Task 多檔_取消_停止請求()
    {
        var (service, twse, _) = await InitManyAsync();
        twse.MonthsWithData["006208"] = new() { M(2026, 11) };
        twse.MonthsWithData["00981A"] = new() { M(2026, 11) };
        using var cts = new CancellationTokenSource();
        twse.OnCall = (stock, _) => { if (stock == "006208") cts.Cancel(); };

        var multi = await service.SyncManyAsync(new[] { "006208", "00981A" }, 1, null, cts.Token);

        Assert.True(multi.Cancelled);
        Assert.DoesNotContain(twse.Calls, c => c.StockId == "00981A");
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public SyncProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }
}
