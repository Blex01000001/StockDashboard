using System.Net;
using Microsoft.Extensions.Configuration;
using StockDashboard.Models;
using StockDashboard.Models.Entities;
using StockDashboard.Models.Patterns;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Implements;
using StockDashboard.Services;

namespace StockDashboard.Tests;

public class MarketIndexApiTests
{
    [Fact]
    public void 解析Yahoo日線_略過null列_日期取UTC_價格取小數2位()
    {
        var rows = MarketIndexApiClient.ParseResponse(Fixtures.Read("yahoo_sox_chart.json"), 200, "SOX");

        // 13 個交易日，其中 2026-10-26 各欄為 null → 12 筆；由舊到新
        Assert.Equal(12, rows.Count);
        Assert.DoesNotContain(rows, r => r.TradeDate == "2026-10-26");
        Assert.Equal(rows.Select(r => r.TradeDate).OrderBy(d => d, StringComparer.Ordinal), rows.Select(r => r.TradeDate));
        Assert.Equal(("2026-10-19", "2026-11-04"), (rows[0].TradeDate, rows[^1].TradeDate));
        Assert.All(rows, r =>
        {
            Assert.Equal("SOX", r.Code);
            Assert.Equal(Math.Round(r.Close, 2), r.Close);   // float 長尾小數已四捨五入
            Assert.True(r.Low < r.Open && r.Open < r.High);
        });
        // 11-02 之後是美東冬令 (時間戳 14:30 UTC)，日期仍是同一天
        Assert.Contains(rows, r => r.TradeDate == "2026-11-02");
    }

    [Fact]
    public void 解析_同一天多筆以最後一筆為準_缺欄位的列略過()
    {
        // 1793000000 = 2026-10-26 07:33 UTC (只用來測日期轉換)；兩筆同一天 → 後者；第三筆缺 close → 略過
        var json = """
            {"chart":{"result":[{"timestamp":[1793000000,1793000100,1793086400],
              "indicators":{"quote":[{"open":[1,2,3],"high":[2,4,5],"low":[0.5,1,2],"close":[1.5,3,null],"volume":[10,20,30]}]}}],"error":null}}
            """;
        var row = Assert.Single(MarketIndexApiClient.ParseResponse(json, 200, "SOX"));
        Assert.Equal(("2026-10-26", 2.0, 3.0, 20L), (row.TradeDate, row.Open, row.Close, row.Volume));
    }

    [Fact]
    public void 解析_區間內沒有交易日時回傳空清單()
    {
        var json = """{"chart":{"result":[{"meta":{"symbol":"^SOX"},"indicators":{"quote":[{}]}}],"error":null}}""";
        Assert.Empty(MarketIndexApiClient.ParseResponse(json, 200, "SOX"));
    }

    [Theory]
    [InlineData(429, "Too Many Requests", "請求過多")]
    [InlineData(404, "{\"chart\":{\"result\":null,\"error\":{\"code\":\"Not Found\",\"description\":\"No data found, symbol may be delisted\"}}}", "No data found")]
    [InlineData(500, "{\"chart\":{\"result\":null,\"error\":null}}", "HTTP 500")]
    [InlineData(200, "<html>blocked</html>", "不是 JSON")]
    [InlineData(200, "{\"foo\":1}", "格式不符")]
    [InlineData(200, "{\"chart\":{\"result\":[],\"error\":null}}", "chart.result")]
    public void 解析_錯誤回應丟出看得懂的訊息(int status, string body, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => MarketIndexApiClient.ParseResponse(body, status, "SOX"));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task 請求用period1_period2指定區間_含to當天_並編碼代號()
    {
        var handler = FakeHttpHandler.Always(Fixtures.Read("yahoo_sox_chart.json"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MarketIndex:ChartUrl"] = "http://x/v8/finance/chart/" }).Build();
        var rows = await new MarketIndexApiClient(new HttpClient(handler), config)
            .GetDailyAsync("SOX", "^SOX", new DateTime(2025, 11, 5), new DateTime(2026, 11, 5));

        Assert.NotEmpty(rows);
        var url = handler.Requests.Single().AbsoluteUri;   // ToString() 會把 %5E 還原成 ^，這裡要看實際送出的編碼
        Assert.StartsWith("http://x/v8/finance/chart/%5ESOX?", url);
        Assert.Contains("interval=1d", url);   // 區間查詢才回日線 (range=max 會降成月線)
        Assert.Contains($"period1={new DateTimeOffset(2025, 11, 5, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()}", url);
        Assert.Contains($"period2={new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()}", url);
    }
}

public class MarketIndexRepositoryAndSyncTests : IDisposable
{
    private readonly TempSqliteDb _db = new();
    public void Dispose() => _db.Dispose();

    private async Task<MarketIndexRepository> InitAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        return new MarketIndexRepository(_db.Configuration);
    }

    private static MarketIndexEntity Bar(string date, double open = 100, double close = 101) =>
        new() { Code = "SOX", TradeDate = date, Open = open, High = Math.Max(open, close) + 5, Low = Math.Min(open, close) - 5, Close = close, Volume = 0 };

    [Fact]
    public async Task 寫入_同日覆蓋_讀回由舊到新_並有摘要()
    {
        var repo = await InitAsync();
        Assert.Equal((null, null, 0), await repo.GetSummaryAsync("SOX"));

        await repo.UpsertAsync(new[] { Bar("2026-11-03"), Bar("2026-11-02") });
        await repo.UpsertAsync(new[] { Bar("2026-11-03", 200, 210), Bar("2026-11-04") });   // 11-03 覆蓋

        var all = (await repo.GetAllAsync("SOX")).ToList();
        Assert.Equal(new[] { "2026-11-02", "2026-11-03", "2026-11-04" }, all.Select(r => r.TradeDate));
        Assert.Equal((200.0, 210.0), (all[1].Open, all[1].Close));
        Assert.Equal(("2026-11-02", "2026-11-04", 3), await repo.GetSummaryAsync("SOX"));
        Assert.Empty(await repo.GetAllAsync("OTHER"));
    }

    [Fact]
    public async Task 讀取失敗時回傳空清單_不影響圖表()
    {
        // 舊資料庫沒有 MarketIndexDaily 表 (沒跑過初始化)
        var repo = new MarketIndexRepository(_db.Configuration);
        Assert.Empty(await repo.GetAllOrEmptyAsync("SOX"));
    }

    [Fact]
    public async Task 同步_抓最近365天_寫入並累積舊資料()
    {
        var repo = await InitAsync();
        await repo.UpsertAsync(new[] { Bar("2024-01-02") });   // 一年前之外、先前累積的資料

        var api = new FakeIndexApi(new List<MarketIndexEntity> { Bar("2026-11-03"), Bar("2026-11-04") });
        var result = await new MarketIndexSyncService(api, repo, () => new DateTime(2026, 11, 5, 15, 30, 0)).SyncSoxAsync(CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal((2, "2026-11-03", "2026-11-04"), (result.RowsWritten, result.FirstDate, result.LastDate));
        Assert.Equal(("SOX", "^SOX", new DateTime(2025, 11, 5), new DateTime(2026, 11, 5)), api.LastCall);
        Assert.Equal(("2024-01-02", "2026-11-04", 3), await repo.GetSummaryAsync("SOX"));
    }

    [Fact]
    public async Task 同步_失敗或沒資料時回報錯誤_不寫入()
    {
        var repo = await InitAsync();

        var failed = await new MarketIndexSyncService(new FakeIndexApi(new InvalidOperationException("Yahoo 回應請求過多 (HTTP 429)")), repo).SyncSoxAsync(CancellationToken.None);
        Assert.Contains("429", failed.Error);

        var empty = await new MarketIndexSyncService(new FakeIndexApi(new List<MarketIndexEntity>()), repo).SyncSoxAsync(CancellationToken.None);
        Assert.NotNull(empty.Error);
        Assert.Equal(0, (await repo.GetSummaryAsync("SOX")).Days);
    }

    private sealed class FakeIndexApi : IMarketIndexApiClient
    {
        private readonly List<MarketIndexEntity>? _rows;
        private readonly Exception? _error;
        public (string Code, string Symbol, DateTime From, DateTime To)? LastCall { get; private set; }
        public FakeIndexApi(List<MarketIndexEntity> rows) => _rows = rows;
        public FakeIndexApi(Exception error) => _error = error;

        public Task<List<MarketIndexEntity>> GetDailyAsync(string code, string symbol, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            LastCall = (code, symbol, from, to);
            return _error != null ? Task.FromException<List<MarketIndexEntity>>(_error) : Task.FromResult(_rows!);
        }
    }
}

public class MarketIndexAlignmentTests
{
    // 美股行情：日期、開、高、低、收
    private static MarketIndexEntity Us(string date, double o, double h, double l, double c) =>
        new() { Code = "SOX", TradeDate = date, Open = o, High = h, Low = l, Close = c };

    private static List<PatternCandle> Candles(IEnumerable<string> dates) => dates.Select(d => new PatternCandle { Date = d }).ToList();

    private static (double?, double?, double?, double?, string?) Of(PatternCandle c) => (c.SoxOpen, c.SoxHigh, c.SoxLow, c.SoxClose, c.SoxDate);

    [Fact]
    public void 一般日對應前一個美股交易日_週一對應週五_美股休市日留空()
    {
        // 台股：10-29 (四) 10-30 (五) 11-02 (一) 11-03 11-04 11-05 11-06 11-09 (一)
        var tw = new[] { "2026-10-29", "2026-10-30", "2026-11-02", "2026-11-03", "2026-11-04", "2026-11-05", "2026-11-06", "2026-11-09" };
        // 美股：10-29 10-30 11-02 11-03 (11-04 休市) 11-05 11-06
        var us = new[]
        {
            Us("2026-10-29", 10, 12, 9, 11), Us("2026-10-30", 20, 22, 19, 21), Us("2026-11-02", 30, 32, 29, 31),
            Us("2026-11-03", 40, 42, 39, 41), Us("2026-11-05", 50, 52, 49, 51), Us("2026-11-06", 60, 62, 59, 61),
        };
        var candles = Candles(tw);

        PatternScanService.ApplyMarketIndex(candles, tw, us);

        Assert.Equal((null, null, null, null, null), Of(candles[0]));             // 10-29：之前沒有資料
        Assert.Equal((10, 12, 9, 11, "2026-10-29"), Of(candles[1]));              // 10-30 ← 美股 10-29
        Assert.Equal((20, 22, 19, 21, "2026-10-30"), Of(candles[2]));             // 週一 11-02 ← 週五 10-30
        Assert.Equal((30, 32, 29, 31, "2026-11-02"), Of(candles[3]));             // 11-03 ← 11-02
        Assert.Equal((40, 42, 39, 41, "2026-11-03"), Of(candles[4]));             // 11-04 ← 11-03
        Assert.Equal((null, null, null, null, null), Of(candles[5]));             // 11-05：美股 11-04 休市 → 留空，不重複 11-03
        Assert.Equal((50, 52, 49, 51, "2026-11-05"), Of(candles[6]));             // 11-06 ← 11-05
        Assert.Equal((60, 62, 59, 61, "2026-11-06"), Of(candles[7]));             // 週一 11-09 ← 週五 11-06
    }

    [Fact]
    public void 台股連假期間的多個美股交易日合併成一根()
    {
        // 台股 11-04、11-05 休市
        var tw = new[] { "2026-11-02", "2026-11-03", "2026-11-06", "2026-11-09" };
        var us = new[]
        {
            Us("2026-11-02", 10, 15, 9, 12),    // → 11-03
            Us("2026-11-03", 20, 30, 18, 25),   // ┐
            Us("2026-11-04", 26, 40, 24, 35),   // ├ 都在台股 11-06 開盤前收盤 → 合併
            Us("2026-11-05", 36, 38, 5, 8),     // ┘
        };
        var candles = Candles(tw);

        PatternScanService.ApplyMarketIndex(candles, tw, us);

        Assert.Equal((10, 15, 9, 12, "2026-11-02"), Of(candles[1]));
        // 合併：開 = 11-03 的開、收 = 11-05 的收、高 = 最大 (40)、低 = 最小 (5)、日期 = 最後一天
        Assert.Equal((20, 40, 5, 8, "2026-11-05"), Of(candles[2]));
        Assert.Equal((null, null, null, null, null), Of(candles[3]));   // 11-09：之後沒有新的美股
    }

    [Fact]
    public void 晚於最後一個台股交易日的美股忽略_不影響合併結果()
    {
        var tw = new[] { "2026-11-02", "2026-11-03" };
        var us = new[] { Us("2026-11-02", 1, 2, 0.5, 1.5), Us("2026-11-03", 3, 4, 2.5, 3.5) };   // 11-03 美股在台股 11-03 之後才輪到
        var candles = Candles(tw);

        PatternScanService.ApplyMarketIndex(candles, tw, us);

        Assert.Equal((null, null, null, null, null), Of(candles[0]));
        Assert.Equal((1, 2, 0.5, 1.5, "2026-11-02"), Of(candles[1]));
    }

    [Fact]
    public void 只填一段窗口_結果與整段一致()
    {
        // 窗口第一根 (11-03) 的前一個台股日 (11-02) 不在窗口內：要靠完整日線的日期才知道區間起點 (這裡是週末後的合併)
        var tw = new[] { "2026-10-30", "2026-11-02", "2026-11-03", "2026-11-04" };
        var us = new[] { Us("2026-10-29", 1, 2, 0, 1), Us("2026-10-30", 5, 9, 4, 8), Us("2026-11-02", 7, 8, 6, 7.5) };
        var all = Candles(tw);
        PatternScanService.ApplyMarketIndex(all, tw, us);

        var window = Candles(tw.Skip(2));   // 只有 11-03、11-04
        PatternScanService.ApplyMarketIndex(window, tw, us);

        Assert.Equal(Of(all[2]), Of(window[0]));
        Assert.Equal(Of(all[3]), Of(window[1]));
        Assert.Equal((7, 8, 6, 7.5, "2026-11-02"), Of(window[0]));
    }

    [Fact]
    public void 沒有指數資料時不填()
    {
        var candles = Candles(new[] { "2026-11-02" });
        PatternScanService.ApplyMarketIndex(candles, new[] { "2026-11-02" }, Array.Empty<MarketIndexEntity>());
        Assert.Null(candles[0].SoxClose);
    }

    [Fact]
    public void 技術圖表依資料填入費半_改顯示筆數時重填()
    {
        var full = Enumerable.Range(0, 40).Select(i => new StockDataPoint
        {
            Date = new DateTime(2026, 9, 1).AddDays(i).ToString("yyyy-MM-dd"), Open = 100, High = 101, Low = 99, Close = 100, Volume = 1000
        }).ToList();
        // 每個日期都有美股：台股日 i 對應美股日 i − 1
        var us = full.Select((c, i) => Us(c.Date, 1000 + i, 1010 + i, 990 + i, 1005 + i)).ToList();

        var data = StockChartService.Build(full, 10, new IndicatorSettings(), Array.Empty<InstitutionalDailyEntity>(), null, us);
        Assert.Equal(10, data.Candles.Count);
        Assert.Equal(1005 + 29, data.Candles[0].SoxClose);   // 窗口第一根是第 30 天 (index 30)，對應 index 29 的美股

        StockChartService.SetWindow(data, 0, new IndicatorSettings());
        Assert.Null(data.Candles[0].SoxClose);                // 全部：第一根之前沒資料
        Assert.Equal(1005 + 38, data.Candles[^1].SoxClose);
    }

    [Fact]
    public void 附圖選項_費半在原油之後且排最後()
    {
        var options = ChartDisplaySettings.SubChartOptions.Select(o => o.Value).ToList();
        Assert.Equal(new[] { "oil", "sox" }, options.TakeLast(2));
        var state = new ChartDisplaySettings();
        state.SubCharts.Add("sox");
        state.SubCharts.Add("macd");
        Assert.Equal(new[] { "volume", "macd", "sox" }, state.OrderedSubCharts);
    }
}
