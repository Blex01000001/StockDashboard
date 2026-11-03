using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Implements;
using StockDashboard.Services;

namespace StockDashboard.Tests;

public class FinMindParsingTests
{
    private static FinMindApiClient Client(FakeHttpHandler handler) => new(new HttpClient(handler));

    [Fact]
    public async Task 買賣超_每天多列合併成一列()
    {
        var handler = FakeHttpHandler.Always(Fixtures.Read("finmind_buysell_2330.json"));
        var rows = await Client(handler).GetInstitutionalTradingAsync("2330", new DateTime(2026, 11, 2), new DateTime(2026, 11, 3));

        Assert.Equal(new[] { "2026-11-02", "2026-11-03" }, rows.Select(r => r.TradeDate));
        var day = rows[0];
        Assert.Equal("2330", day.StockId);
        Assert.Equal(18_715_868 - 33_720_882, day.ForeignNet);
        Assert.Equal(0, day.ForeignDealerNet);
        Assert.Equal(3_754_223 - 72_000, day.TrustNet);
        Assert.Equal((567_000 - 548_100) + (2_029_402 - 162_409), day.DealerNet);   // 自行買賣 + 避險
        Assert.Equal(-9_436_898, day.TotalNet);
        Assert.Equal(day.TotalNet, day.ForeignTotalNet + day.TrustNet + day.DealerNet);

        var url = handler.Requests.Single().ToString();
        Assert.Contains("dataset=TaiwanStockInstitutionalInvestorsBuySell", url);
        Assert.Contains("data_id=2330", url);
        Assert.Contains("start_date=2026-11-02", url);
        Assert.Contains("end_date=2026-11-03", url);
    }

    [Fact]
    public void 舊制_自營商合併_沒有外資自營商()
    {
        // 2014-12-01 前只有合併的 Dealer；2017-12-18 前沒有 Foreign_Dealer_Self
        var json = """
            [{"date":"2013-05-02","stock_id":"2330","buy":500,"name":"Foreign_Investor","sell":100},
             {"date":"2013-05-02","stock_id":"2330","buy":30,"name":"Investment_Trust","sell":50},
             {"date":"2013-05-02","stock_id":"2330","buy":70,"name":"Dealer","sell":0}]
            """;
        var data = JsonDocument.Parse(json).RootElement.EnumerateArray().Select(e => e.Clone()).ToList();

        var row = Assert.Single(FinMindApiClient.ParseTrading("2330", data));
        Assert.Equal(400, row.ForeignNet);
        Assert.Null(row.ForeignDealerNet);
        Assert.Equal(400, row.ForeignTotalNet);     // 外資合計 = 外資 (當時已含外資自營商)
        Assert.Equal(-20, row.TrustNet);
        Assert.Equal(70, row.DealerNet);
        Assert.Equal(450, row.TotalNet);
    }

    [Fact]
    public async Task 外資持股()
    {
        var handler = FakeHttpHandler.Always(Fixtures.Read("finmind_shareholding_2330.json"));
        var rows = await Client(handler).GetForeignHoldingAsync("2330", new DateTime(2026, 11, 1), new DateTime(2026, 11, 3));

        Assert.Equal(2, rows.Count);
        Assert.Equal("2026-11-02", rows[0].TradeDate);
        Assert.Equal(25_932_370_067, rows[0].IssuedShares);
        Assert.Equal(17_751_126_705, rows[0].ForeignShares);
        Assert.Equal(68.45, rows[0].ForeignHoldingPct);
        Assert.Null(rows[0].TotalNet);
        Assert.Contains("dataset=TaiwanStockShareholding", handler.Requests.Single().ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]          // 實際可能以 HTTP 200 回傳
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task IP被封鎖_拋出封鎖例外(HttpStatusCode httpStatus)
    {
        var handler = FakeHttpHandler.Always(Fixtures.Read("finmind_ip_banned.json"), status: httpStatus);
        var ex = await Assert.ThrowsAsync<FinMindBlockedException>(() =>
            Client(handler).GetInstitutionalTradingAsync("2330", new DateTime(2026, 11, 1), new DateTime(2026, 11, 3)));
        Assert.Contains("封鎖", ex.Message);
        Assert.Contains("ip banned", ex.Message);
    }

    [Fact]
    public void 達請求上限_拋出封鎖例外()
    {
        var ex = Assert.Throws<FinMindBlockedException>(() =>
            FinMindApiClient.ParseResponse("""{"msg":"Requests reach the upper limit.","status":402}""", 402));
        Assert.Contains("上限", ex.Message);
    }

    [Fact]
    public void 其他錯誤與非JSON()
    {
        Assert.Throws<InvalidOperationException>(() => FinMindApiClient.ParseResponse("""{"msg":"parameter error","status":400}""", 400));
        Assert.Throws<InvalidOperationException>(() => FinMindApiClient.ParseResponse("<html>gateway</html>", 502));
        Assert.Empty(FinMindApiClient.ParseResponse("""{"msg":"success","status":200,"data":[]}""", 200));
    }
}

public class FinMindSyncServiceTests : IDisposable
{
    private readonly TempSqliteDb _db = new();
    public void Dispose() => _db.Dispose();
    private static readonly DateTime Today = new(2026, 11, 3);

    private sealed class FakeFinMind : IFinMindApiClient
    {
        public List<string> Calls { get; } = new();
        public string? BanOn { get; set; }

        public Task<List<InstitutionalDailyEntity>> GetInstitutionalTradingAsync(string stockId, DateTime from, DateTime to, CancellationToken ct = default) =>
            Fetch("買賣超", stockId, from, to, () => new InstitutionalDailyEntity { StockId = stockId, TradeDate = "2026-11-02", ForeignNet = 10, TotalNet = 10 });

        public Task<List<InstitutionalDailyEntity>> GetForeignHoldingAsync(string stockId, DateTime from, DateTime to, CancellationToken ct = default) =>
            Fetch("持股", stockId, from, to, () => new InstitutionalDailyEntity { StockId = stockId, TradeDate = "2026-11-02", ForeignHoldingPct = 50 });

        private Task<List<InstitutionalDailyEntity>> Fetch(string kind, string stockId, DateTime from, DateTime to, Func<InstitutionalDailyEntity> make)
        {
            Calls.Add($"{kind} {stockId} {from:yyyy-MM-dd}~{to:yyyy-MM-dd}");
            if (BanOn == stockId) throw new FinMindBlockedException("FinMind 已封鎖此 IP（ip banned）");
            return Task.FromResult(new List<InstitutionalDailyEntity> { make() });
        }
    }

    private async Task<(InstitutionalRepository, FakeFinMind)> InitAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, NullLogger.Instance);
        return (new InstitutionalRepository(_db.Configuration), new FakeFinMind());
    }

    private static FinMindSyncService Service(FakeFinMind api, InstitutionalRepository repo) => new(api, repo, 0, () => Today.AddHours(15));

    [Fact]
    public void 起始日()
    {
        Assert.Equal(new DateTime(2012, 5, 2), FinMindSyncService.StartDate(null, false));
        Assert.Equal(new DateTime(2026, 10, 27), FinMindSyncService.StartDate("2026-11-03", false));
        Assert.Equal(new DateTime(2012, 5, 2), FinMindSyncService.StartDate("2026-11-03", true));
        Assert.Equal(new DateTime(2012, 5, 2), FinMindSyncService.StartDate("2012-05-05", false));   // 不早於最早日期
    }

    [Fact]
    public async Task 第一次全抓_之後只補最近_勾選完整重新下載則全抓()
    {
        var (repo, api) = await InitAsync();

        var first = await Service(api, repo).SyncAsync(new[] { "2330", "0050" }, false, null, default);
        Assert.Equal(2, first.StocksDone);
        Assert.Equal(4, first.Requests);
        Assert.Equal(new[]
        {
            "買賣超 2330 2012-05-02~2026-11-03", "持股 2330 2012-05-02~2026-11-03",
            "買賣超 0050 2012-05-02~2026-11-03", "持股 0050 2012-05-02~2026-11-03",
        }, api.Calls);
        var row = Assert.Single(await repo.GetByStockAsync("2330"));
        Assert.Equal((10L, 50.0), (row.ForeignNet!.Value, row.ForeignHoldingPct!.Value));   // 兩個資料集寫入同一列
        Assert.Equal("2026-11-03", (await repo.GetFinMindSyncedAsync(FinMindSyncService.TradingDataset))["2330"]);

        api.Calls.Clear();
        await Service(api, repo).SyncAsync(new[] { "2330" }, false, null, default);
        Assert.Equal(new[] { "買賣超 2330 2026-10-27~2026-11-03", "持股 2330 2026-10-27~2026-11-03" }, api.Calls);

        api.Calls.Clear();
        await Service(api, repo).SyncAsync(new[] { "2330" }, true, null, default);
        Assert.All(api.Calls, c => Assert.Contains("2012-05-02", c));
    }

    [Fact]
    public async Task IP被封鎖立即停止_之前的股票保留()
    {
        var (repo, api) = await InitAsync();
        api.BanOn = "0050";

        var result = await Service(api, repo).SyncAsync(new[] { "2330", "0050", "2317" }, false, null, default);

        Assert.True(result.Blocked);
        Assert.Contains("ip banned", result.Error);
        Assert.Equal(1, result.StocksDone);
        Assert.Equal(3, api.Calls.Count);                                     // 0050 第一個請求後就停止
        Assert.DoesNotContain(api.Calls, c => c.Contains("2317"));
        Assert.Single(await repo.GetByStockAsync("2330"));
        Assert.False((await repo.GetFinMindSyncedAsync(FinMindSyncService.TradingDataset)).ContainsKey("0050"));
    }

    [Fact]
    public async Task 可以中途停止()
    {
        var (repo, api) = await InitAsync();
        using var cts = new CancellationTokenSource();
        var progress = new Progress(p => { if (p.Done == 1) cts.Cancel(); });

        var result = await Service(api, repo).SyncAsync(new[] { "2330", "0050" }, false, progress, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(1, result.StocksDone);
        Assert.Equal(2, api.Calls.Count);
    }

    private sealed class Progress(Action<FinMindSyncProgress> onReport) : IProgress<FinMindSyncProgress>
    {
        public void Report(FinMindSyncProgress value) => onReport(value);
    }
}
