using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StockDashboard.Models.Entities;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Implements;
using StockDashboard.Services;
using StockDashboard.Models.Patterns;

namespace StockDashboard.Tests;

public class InstitutionalParsingTests
{
    private static TwseApiClient Client(string body) => new(new HttpClient(FakeHttpHandler.Always(body)));

    [Fact]
    public async Task 解析三大法人買賣超日報()
    {
        var handler = FakeHttpHandler.Always(Fixtures.Read("t86_20261030.json"));
        var rows = await new TwseApiClient(new HttpClient(handler)).GetInstitutionalTradingAsync(new DateTime(2026, 10, 30));

        Assert.Equal(3, rows.Count);
        var yangming = rows.Single(r => r.StockId == "2609");
        Assert.Equal("2026-10-30", yangming.TradeDate);
        Assert.Equal(16_598_249, yangming.ForeignNet);
        Assert.Equal(0, yangming.ForeignDealerNet);
        Assert.Equal(-30_000, yangming.TrustNet);
        Assert.Equal(284_025, yangming.DealerNet);
        Assert.Equal(16_852_274, yangming.TotalNet);
        // 外資合計 + 投信 + 自營商 = 三大法人
        Assert.Equal(yangming.TotalNet, yangming.ForeignTotalNet + yangming.TrustNet + yangming.DealerNet);
        Assert.Equal(-7_782_000, rows.Single(r => r.StockId == "00712").DealerNet);

        var url = handler.Requests.Single().ToString();
        Assert.StartsWith(TwseApiClient.DefaultT86Url, url);
        Assert.Contains("date=20261030", url);
        Assert.Contains("selectType=ALLBUT0999", url);
    }

    [Fact]
    public async Task 解析外資持股_數字與字串混合的儲存格()
    {
        var rows = await Client(Fixtures.Read("mi_qfiis_20261030.json")).GetForeignHoldingAsync(new DateTime(2026, 10, 30));

        Assert.Equal(4, rows.Count);
        var hon = rows.Single(r => r.StockId == "2317");
        Assert.Equal(14_028_648_626, hon.IssuedShares);
        Assert.Equal(5_610_272_029, hon.ForeignShares);
        Assert.Equal(39.99, hon.ForeignHoldingPct);   // JSON 數字 (非字串)
        Assert.Equal(0.01, rows.Single(r => r.StockId == "2321").ForeignHoldingPct);
        Assert.Null(hon.TotalNet);                     // 持股來源不含買賣超
    }

    [Fact]
    public async Task 非交易日回傳空清單()
    {
        Assert.Empty(await Client(Fixtures.Read("t86_no_data.json")).GetInstitutionalTradingAsync(new DateTime(2026, 11, 1)));
        Assert.Empty(await Client(Fixtures.Read("t86_no_data.json")).GetForeignHoldingAsync(new DateTime(2026, 11, 1)));
    }

    [Fact]
    public async Task 舊格式_外資欄位名稱不同且自營商沒有合計欄位()
    {
        var body = """
            {"stat":"OK","fields":["證券代號","證券名稱","外資買賣超股數","投信買賣超股數","自營商買賣超股數(自行買賣)","自營商買賣超股數(避險)","三大法人買賣超股數"],
             "data":[["2330","台積電","1,000","-200","50","-30","820"]]}
            """;
        var row = Assert.Single(await Client(body).GetInstitutionalTradingAsync(new DateTime(2016, 1, 4)));
        Assert.Equal(1000, row.ForeignNet);
        Assert.Null(row.ForeignDealerNet);
        Assert.Equal(20, row.DealerNet);               // 50 + (−30)
        Assert.Equal(1000, row.ForeignTotalNet);
    }

    [Fact]
    public async Task 欄位無法辨識時拋出錯誤()
    {
        var body = """{"stat":"OK","fields":["代號","名稱"],"data":[["2330","台積電"]]}""";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(body).GetInstitutionalTradingAsync(new DateTime(2026, 10, 30)));
    }
}

public class InstitutionalRepositoryTests : IDisposable
{
    private readonly TempSqliteDb _db = new();
    public void Dispose() => _db.Dispose();

    private async Task<InstitutionalRepository> InitAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, NullLogger.Instance);
        return new InstitutionalRepository(_db.Configuration);
    }

    [Fact]
    public async Task 買賣超與持股分開寫入同一列_互不覆蓋()
    {
        var repo = await InitAsync();
        await repo.UpsertTradingAsync(new[] { new InstitutionalDailyEntity { StockId = "2330", TradeDate = "2026-10-30", ForeignNet = 100, TrustNet = 5, DealerNet = -3, TotalNet = 102 } });
        await repo.UpsertHoldingAsync(new[] { new InstitutionalDailyEntity { StockId = "2330", TradeDate = "2026-10-30", IssuedShares = 1000, ForeignShares = 700, ForeignHoldingPct = 70 } });
        await repo.UpsertTradingAsync(new[] { new InstitutionalDailyEntity { StockId = "2330", TradeDate = "2026-10-30", ForeignNet = 200, TotalNet = 202 } });

        var row = Assert.Single(await repo.GetByStockAsync("2330"));
        Assert.Equal(200, row.ForeignNet);
        Assert.Equal(202, row.TotalNet);
        Assert.Equal(70, row.ForeignHoldingPct);       // 持股沒被買賣超的寫入清掉
        Assert.Equal(1000, row.IssuedShares);
    }

    [Fact]
    public async Task 同步紀錄與統計()
    {
        var repo = await InitAsync();
        await repo.LogCheckAsync("2026-10-30", "T86", true, new DateTime(2026, 10, 31, 9, 0, 0));
        await repo.LogCheckAsync("2026-11-01", "T86", false, new DateTime(2026, 11, 2, 9, 0, 0));
        await repo.UpsertTradingAsync(new[]
        {
            new InstitutionalDailyEntity { StockId = "2330", TradeDate = "2026-10-29", TotalNet = 1 },
            new InstitutionalDailyEntity { StockId = "0050", TradeDate = "2026-10-30", TotalNet = 2 },
            new InstitutionalDailyEntity { StockId = "2330", TradeDate = "2026-10-30", TotalNet = 3 },
        });

        var log = await repo.GetCheckedDatesAsync("T86");
        Assert.True(log["2026-10-30"].HasData);
        Assert.False(log["2026-11-01"].HasData);
        Assert.Equal(new DateTime(2026, 11, 2, 9, 0, 0), log["2026-11-01"].CheckedAt);
        Assert.Empty(await repo.GetCheckedDatesAsync("MI_QFIIS"));
        Assert.Equal(("2026-10-29", "2026-10-30", 2), await repo.GetSummaryAsync());
        Assert.Equal(new[] { "2026-10-29", "2026-10-30" }, (await repo.GetByStockAsync("2330")).Select(r => r.TradeDate));
    }
}

public class InstitutionalSyncServiceTests : IDisposable
{
    private readonly TempSqliteDb _db = new();
    public void Dispose() => _db.Dispose();

    /// <summary>假證交所：指定日期有資料，其餘為非交易日；可指定某天拋出錯誤</summary>
    private sealed class FakeTwse : ITwseApiClient
    {
        public HashSet<DateTime> TradingDays { get; } = new();
        public DateTime? FailOn { get; set; }
        public List<string> Calls { get; } = new();

        public Task<List<InstitutionalDailyEntity>> GetInstitutionalTradingAsync(DateTime date, CancellationToken ct = default) => Fetch("T86", date,
            () => new InstitutionalDailyEntity { StockId = "2330", TradeDate = date.ToString("yyyy-MM-dd"), ForeignNet = date.Day, TotalNet = date.Day });

        public Task<List<InstitutionalDailyEntity>> GetForeignHoldingAsync(DateTime date, CancellationToken ct = default) => Fetch("QFIIS", date,
            () => new InstitutionalDailyEntity { StockId = "2330", TradeDate = date.ToString("yyyy-MM-dd"), ForeignHoldingPct = 70 });

        private Task<List<InstitutionalDailyEntity>> Fetch(string source, DateTime date, Func<InstitutionalDailyEntity> make)
        {
            Calls.Add($"{source} {date:MM-dd}");
            if (FailOn == date) throw new HttpRequestException("證交所回應 HTTP 403 Forbidden");
            return Task.FromResult(TradingDays.Contains(date) ? new List<InstitutionalDailyEntity> { make() } : new List<InstitutionalDailyEntity>());
        }

        public Task<List<StockDailyPriceEntity>> GetMonthlyPricesAsync(string stockId, DateTime month, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<StockNameEntity>> GetListedStocksAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private async Task<(InstitutionalRepository Repo, FakeTwse Twse)> InitAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, NullLogger.Instance);
        var twse = new FakeTwse();
        // 2026-10-26 (一) ~ 10-30 (五) 為交易日
        for (int d = 26; d <= 30; d++) twse.TradingDays.Add(new DateTime(2026, 10, d));
        return (new InstitutionalRepository(_db.Configuration), twse);
    }

    private static InstitutionalSyncService Service(FakeTwse twse, InstitutionalRepository repo, DateTime now) => new(twse, repo, 0, () => now);

    [Fact]
    public void 平日由新到舊()
    {
        var days = InstitutionalSyncService.Weekdays(new DateTime(2026, 10, 30), new DateTime(2026, 11, 2));
        Assert.Equal(new[] { new DateTime(2026, 11, 2), new DateTime(2026, 10, 30) }, days);   // 略過週六日
    }

    [Fact]
    public async Task 同步一週_略過週末_每天查兩個來源()
    {
        var (repo, twse) = await InitAsync();
        var result = await Service(twse, repo, new DateTime(2026, 11, 2, 18, 0, 0)).SyncAsync(new DateTime(2026, 10, 26), new DateTime(2026, 11, 1), null, default);

        Assert.Equal(5, result.DaysWithData);
        Assert.Equal(10, result.Requests);
        Assert.Equal(10, result.RowsWritten);
        Assert.Equal("T86 10-30", twse.Calls[0]);                 // 由新到舊
        Assert.DoesNotContain(twse.Calls, c => c.EndsWith("10-31") || c.EndsWith("11-01"));
        var row = (await repo.GetByStockAsync("2330")).Last();
        Assert.Equal((30L, 70.0), (row.ForeignNet!.Value, row.ForeignHoldingPct!.Value));
    }

    [Fact]
    public async Task 再次同步時_已有資料與已確認的假日都略過()
    {
        var (repo, twse) = await InitAsync();
        twse.TradingDays.Remove(new DateTime(2026, 10, 28));   // 10-28 當作假日
        await Service(twse, repo, new DateTime(2026, 11, 2)).SyncAsync(new DateTime(2026, 10, 26), new DateTime(2026, 10, 30), null, default);
        twse.Calls.Clear();

        var result = await Service(twse, repo, new DateTime(2026, 11, 3)).SyncAsync(new DateTime(2026, 10, 26), new DateTime(2026, 10, 30), null, default);

        Assert.Empty(twse.Calls);
        Assert.Equal(0, result.Requests);
        Assert.Equal(10, result.SkippedChecks);
        Assert.Equal(0, result.DaysWithData);   // 只算這次新抓到的
    }

    [Fact]
    public async Task 當天查無資料_隔天同步會再查一次()
    {
        var (repo, twse) = await InitAsync();
        var today = new DateTime(2026, 11, 2);   // 當天下午資料尚未公布
        await Service(twse, repo, today.AddHours(10)).SyncAsync(today, today, null, default);
        twse.TradingDays.Add(today);             // 晚上公布了
        twse.Calls.Clear();

        var result = await Service(twse, repo, today.AddHours(20)).SyncAsync(today, today, null, default);

        Assert.Equal(new[] { "T86 11-02", "QFIIS 11-02" }, twse.Calls);
        Assert.Equal(1, result.DaysWithData);
    }

    [Fact]
    public async Task 遇到錯誤立即停止_已完成的日期保留()
    {
        var (repo, twse) = await InitAsync();
        twse.FailOn = new DateTime(2026, 10, 28);

        var result = await Service(twse, repo, new DateTime(2026, 11, 2)).SyncAsync(new DateTime(2026, 10, 26), new DateTime(2026, 10, 30), null, default);

        Assert.Contains("2026-10-28", result.Error);
        Assert.Contains("403", result.Error);
        Assert.Equal(2, result.DaysWithData);                                   // 10-30、10-29
        Assert.DoesNotContain(twse.Calls, c => c.EndsWith("10-27"));            // 不再繼續請求
        Assert.Equal(new[] { "2026-10-29", "2026-10-30" }, (await repo.GetByStockAsync("2330")).Select(r => r.TradeDate));
    }

    [Fact]
    public async Task 可以中途停止()
    {
        var (repo, twse) = await InitAsync();
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.Done == 1) cts.Cancel(); });

        var result = await Service(twse, repo, new DateTime(2026, 11, 2)).SyncAsync(new DateTime(2026, 10, 26), new DateTime(2026, 10, 30), progress, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(1, result.DaysWithData);
        Assert.Equal(2, twse.Calls.Count);
    }

    // 同步回報進度 (Progress<T> 會丟到其他執行緒，測試用同步版)
    private sealed class SyncProgress(Action<InstitutionalSyncProgress> onReport) : IProgress<InstitutionalSyncProgress>
    {
        public void Report(InstitutionalSyncProgress value) => onReport(value);
    }
}

public class ApplyInstitutionalTests
{
    [Fact]
    public void FillsCandlesByDate_AndLeavesMissingDatesNull()
    {
        var match = new PatternMatch
        {
            Candles =
            {
                new PatternCandle { Date = "2026-10-29" },
                new PatternCandle { Date = "2026-10-30" },
            }
        };
        var rows = new[]
        {
            new InstitutionalDailyEntity
            {
                StockId = "2609", TradeDate = "2026-10-30",
                ForeignNet = 16_598_249, ForeignDealerNet = 0, TrustNet = -30_000, DealerNet = 284_025, TotalNet = 16_852_274,
                ForeignHoldingPct = 12.34
            },
            new InstitutionalDailyEntity { StockId = "2609", TradeDate = "2026-10-01", TotalNet = 1 },   // 不在小卡範圍
        };

        PatternScanService.ApplyInstitutional(new[] { match }, rows);

        var missing = match.Candles[0];
        Assert.Null(missing.TotalNet);
        Assert.Null(missing.ForeignNet);
        Assert.Null(missing.ForeignHoldingPct);

        var day = match.Candles[1];
        Assert.Equal(16_852_274, day.TotalNet);
        Assert.Equal(16_598_249, day.ForeignNet);     // 外資 = 外陸資 + 外資自營商
        Assert.Equal(-30_000, day.TrustNet);
        Assert.Equal(284_025, day.DealerNet);
        Assert.Equal(12.34, day.ForeignHoldingPct);
        Assert.Equal(day.TotalNet, day.ForeignNet + day.TrustNet + day.DealerNet);
    }

    [Fact]
    public void HoldingOnlyRow_LeavesNetNull()
    {
        var match = new PatternMatch { Candles = { new PatternCandle { Date = "2026-10-30" } } };
        PatternScanService.ApplyInstitutional(new[] { match },
            new[] { new InstitutionalDailyEntity { StockId = "2330", TradeDate = "2026-10-30", ForeignHoldingPct = 70.1 } });

        Assert.Null(match.Candles[0].ForeignNet);
        Assert.Equal(70.1, match.Candles[0].ForeignHoldingPct);
    }
}
