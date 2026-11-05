using System.Net;
using Microsoft.Extensions.Configuration;
using StockDashboard.Models.Entities;
using StockDashboard.Models.Patterns;
using StockDashboard.Repositories;
using StockDashboard.Repositories.Implements;
using StockDashboard.Services;

namespace StockDashboard.Tests;

public class OilPriceApiTests
{
    private static IConfiguration Config(string? apiKey = "test-key") => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["OilPrice:ApiKey"] = apiKey }).Build();

    [Fact]
    public void 解析past_year_只取每日均價_日期取UTC日期_由舊到新()
    {
        var rows = OilPriceApiClient.ParseResponse(Fixtures.Read("oilprice_past_year.json"), 200);

        Assert.Equal(12, rows.Count);
        Assert.Equal(rows.Select(r => r.TradeDate).OrderBy(d => d, StringComparer.Ordinal), rows.Select(r => r.TradeDate));
        Assert.All(rows, r => Assert.Equal("BRENT_CRUDE_USD", r.Code));
        // 實際回應：2026-11-04 100.34、2026-11-03 104.78
        Assert.Equal(100.34, rows.Single(r => r.TradeDate == "2026-11-04").Price);
        Assert.Equal(104.78, rows.Single(r => r.TradeDate == "2026-11-03").Price);
    }

    [Fact]
    public void 解析_略過盤中報價_同一天以最後一筆為準()
    {
        var json = """
            {"status":"success","data":{"prices":[
              {"price":97.18,"code":"BRENT_CRUDE_USD","created_at":"2026-11-05T13:36:14.483Z","type":"spot_price"},
              {"price":10.0,"code":"BRENT_CRUDE_USD","created_at":"2026-11-04T00:00:00.000Z","type":"daily_average"},
              {"price":20.0,"code":"BRENT_CRUDE_USD","created_at":"2026-11-04T00:00:00.000Z","type":"daily_average"},
              {"price":"abc","code":"BRENT_CRUDE_USD","created_at":"2026-11-03T00:00:00.000Z","type":"daily_average"}
            ]}}
            """;
        var row = Assert.Single(OilPriceApiClient.ParseResponse(json, 200));
        Assert.Equal(("2026-11-04", 20.0), (row.TradeDate, row.Price));
    }

    [Theory]
    [InlineData(401, "{\"status\":\"error\"}", "API Key")]
    [InlineData(403, "{}", "API Key")]
    [InlineData(429, "{}", "上限")]
    [InlineData(500, "{\"status\":\"error\"}", "回應錯誤")]
    [InlineData(200, "<html>busy</html>", "不是 JSON")]
    [InlineData(200, "{\"status\":\"success\",\"data\":{}}", "格式不符")]
    public void 解析_錯誤回應丟出看得懂的訊息(int status, string body, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => OilPriceApiClient.ParseResponse(body, status));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task 請求帶API_Key標頭與Brent代號_且錯誤訊息不含Key()
    {
        string? auth = null;
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Response(Fixtures.Read("oilprice_past_year.json")));
        var http = new HttpClient(new CaptureAuthHandler(handler, a => auth = a));
        var rows = await new OilPriceApiClient(http, Config("secret123")).GetPastYearAsync();

        Assert.Equal("Token secret123", auth);
        Assert.NotEmpty(rows);
        Assert.Contains("by_code=BRENT_CRUDE_USD", handler.Requests.Single().ToString());

        var unauthorized = new FakeHttpHandler(_ => FakeHttpHandler.Response("{}", status: HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new OilPriceApiClient(new HttpClient(unauthorized), Config("secret123")).GetPastYearAsync());
        Assert.DoesNotContain("secret123", ex.Message);
    }

    [Fact]
    public async Task 沒設定API_Key_不發請求並提示設定()
    {
        var handler = FakeHttpHandler.Always("{}");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new OilPriceApiClient(new HttpClient(handler), Config(null)).GetPastYearAsync());
        Assert.Contains("OilPrice:ApiKey", ex.Message);
        Assert.Empty(handler.Requests);
    }

    // 把 Authorization 標頭交給測試檢查，再轉給假的處理器
    private sealed class CaptureAuthHandler(FakeHttpHandler inner, Action<string?> capture) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture(request.Headers.Authorization?.ToString());
            return base.SendAsync(request, cancellationToken);
        }
    }
}

public class OilPriceRepositoryAndSyncTests : IDisposable
{
    private readonly TempSqliteDb _db = new();
    public void Dispose() => _db.Dispose();

    private async Task<OilPriceRepository> InitAsync()
    {
        await DatabaseInitializer.InitializeAsync(_db.Configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        return new OilPriceRepository(_db.Configuration);
    }

    private static OilPriceEntity Oil(string date, double price) => new() { Code = "BRENT_CRUDE_USD", TradeDate = date, Price = price };

    [Fact]
    public async Task 寫入_同日覆蓋_讀回由舊到新_並有摘要()
    {
        var repo = await InitAsync();
        Assert.Equal((null, null, 0), await repo.GetSummaryAsync("BRENT_CRUDE_USD"));

        await repo.UpsertAsync(new[] { Oil("2026-11-03", 104.78), Oil("2026-11-02", 107.33) });
        await repo.UpsertAsync(new[] { Oil("2026-11-03", 105.0), Oil("2026-11-04", 100.34) });   // 11-03 覆蓋

        var all = (await repo.GetAllAsync("BRENT_CRUDE_USD")).ToList();
        Assert.Equal(new[] { "2026-11-02", "2026-11-03", "2026-11-04" }, all.Select(r => r.TradeDate));
        Assert.Equal(105.0, all[1].Price);
        Assert.Equal(("2026-11-02", "2026-11-04", 3), await repo.GetSummaryAsync("BRENT_CRUDE_USD"));
        Assert.Empty(await repo.GetAllAsync("OTHER"));
    }

    [Fact]
    public async Task 讀取失敗時回傳空清單_不影響圖表()
    {
        // 舊資料庫沒有 OilPriceDaily 表 (沒跑過初始化)
        var repo = new OilPriceRepository(_db.Configuration);
        Assert.Empty(await repo.GetAllOrEmptyAsync("BRENT_CRUDE_USD"));
    }

    [Fact]
    public async Task 同步_寫入資料庫_累積舊資料不刪除()
    {
        var repo = await InitAsync();
        await repo.UpsertAsync(new[] { Oil("2024-01-02", 77.0) });   // 一年前之外、先前累積的資料

        var api = new FakeOilApi(new List<OilPriceEntity> { Oil("2026-11-03", 104.78), Oil("2026-11-04", 100.34) });
        var result = await new OilPriceSyncService(api, repo).SyncAsync(CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal((2, "2026-11-03", "2026-11-04"), (result.RowsWritten, result.FirstDate, result.LastDate));
        Assert.Equal(("2024-01-02", "2026-11-04", 3), await repo.GetSummaryAsync("BRENT_CRUDE_USD"));
    }

    [Fact]
    public async Task 同步_失敗或沒資料時回報錯誤_不寫入()
    {
        var repo = await InitAsync();

        var failed = await new OilPriceSyncService(new FakeOilApi(new InvalidOperationException("API Key 無效")), repo).SyncAsync(CancellationToken.None);
        Assert.Contains("API Key", failed.Error);

        var empty = await new OilPriceSyncService(new FakeOilApi(new List<OilPriceEntity>()), repo).SyncAsync(CancellationToken.None);
        Assert.NotNull(empty.Error);
        Assert.Equal(0, (await repo.GetSummaryAsync("BRENT_CRUDE_USD")).Days);
    }

    private sealed class FakeOilApi : IOilPriceApiClient
    {
        private readonly List<OilPriceEntity>? _rows;
        private readonly Exception? _error;
        public FakeOilApi(List<OilPriceEntity> rows) => _rows = rows;
        public FakeOilApi(Exception error) => _error = error;
        public Task<List<OilPriceEntity>> GetPastYearAsync(CancellationToken cancellationToken = default) =>
            _error != null ? Task.FromException<List<OilPriceEntity>>(_error) : Task.FromResult(_rows!);
    }
}

public class OilPriceApplyTests
{
    private static OilPriceEntity Oil(string date, double price) => new() { Code = "BRENT_CRUDE_USD", TradeDate = date, Price = price };
    private static PatternCandle Candle(string date) => new() { Date = date };

    [Fact]
    public void 每根K棒取當天之前最近一筆_休市日沿用前值_同一天不採用()
    {
        // 油價：週一 10、週二 20 (週三沒有) 、週四 40
        var oil = new[] { Oil("2026-11-02", 10), Oil("2026-11-03", 20), Oil("2026-11-05", 40) };
        var candles = new[] { Candle("2026-11-01"), Candle("2026-11-02"), Candle("2026-11-03"), Candle("2026-11-04"), Candle("2026-11-05"), Candle("2026-11-06") };

        PatternScanService.ApplyOil(candles, oil);

        Assert.Equal(new double?[] { null, null, 10, 20, 20, 40 }, candles.Select(c => c.Oil));
        // 11-02 當天不用 11-02 的值 (均價涵蓋台股收盤之後)，11-01 之前沒資料為 null
    }

    [Fact]
    public void 沒有原油資料時不填()
    {
        var candles = new[] { Candle("2026-11-02") };
        PatternScanService.ApplyOil(candles, Array.Empty<OilPriceEntity>());
        Assert.Null(candles[0].Oil);
    }

    [Fact]
    public void 技術圖表依設定填入原油_改顯示筆數時重填()
    {
        var full = Enumerable.Range(0, 60).Select(i => new StockDashboard.Models.StockDataPoint
        {
            Date = new DateTime(2026, 9, 1).AddDays(i).ToString("yyyy-MM-dd"), Open = 100, High = 101, Low = 99, Close = 100, Volume = 1000
        }).ToList();
        var oil = full.Select((c, i) => Oil(c.Date, 50 + i)).ToList();

        var data = StockChartService.Build(full, 20, new IndicatorSettings(), Array.Empty<InstitutionalDailyEntity>(), oil);
        Assert.Equal(20, data.Candles.Count);
        Assert.Equal(50 + 40 - 1, data.Candles[0].Oil);   // 第 41 根 (index 40) 的前一天 = index 39 的價格

        StockChartService.SetWindow(data, 0, new IndicatorSettings());
        Assert.Null(data.Candles[0].Oil);                  // 全部：第一根之前沒資料
        Assert.Equal(50 + 58, data.Candles[^1].Oil);
    }

    [Fact]
    public void 附圖選項有原油且排在最後()
    {
        Assert.Equal("oil", ChartDisplaySettings.SubChartOptions[^1].Value);
        var state = new ChartDisplaySettings();
        state.SubCharts.Add("oil");
        state.SubCharts.Add("dealer");
        Assert.Equal(new[] { "volume", "dealer", "oil" }, state.OrderedSubCharts);
    }
}

public class LocalPathOpenerTests
{
    [Fact]
    public void 各系統的開啟指令()
    {
        var winFile = LocalPathOpener.BuildStartInfo(@"C:\app\appsettings.json", false, isWindows: true, isMac: false);
        Assert.Equal(("notepad.exe", @"C:\app\appsettings.json"), (winFile.FileName, Assert.Single(winFile.ArgumentList)));

        var winFolder = LocalPathOpener.BuildStartInfo(@"C:\app", true, isWindows: true, isMac: false);
        Assert.Equal("explorer.exe", winFolder.FileName);

        var macFile = LocalPathOpener.BuildStartInfo("/app/appsettings.json", false, isWindows: false, isMac: true);
        Assert.Equal("open", macFile.FileName);
        Assert.Equal(new[] { "-t", "/app/appsettings.json" }, macFile.ArgumentList.ToArray());

        var linux = LocalPathOpener.BuildStartInfo("/app", true, isWindows: false, isMac: false);
        Assert.Equal("xdg-open", linux.FileName);
    }

    [Fact]
    public void 路徑含空白時當成單一參數_不經過命令列字串()
    {
        var info = LocalPathOpener.BuildStartInfo(@"C:\My Apps\Stock Dashboard\appsettings.json", false, true, false);
        Assert.Single(info.ArgumentList);
        Assert.True(string.IsNullOrEmpty(info.Arguments));
    }

    [Fact]
    public void 設定檔不存在時提示複製範本()
    {
        var env = new FakeEnv(Path.Combine(Path.GetTempPath(), $"no-settings-{Guid.NewGuid():N}"));
        var opener = new LocalPathOpener(env);
        Assert.False(opener.SettingsFileExists);
        var ex = Assert.Throws<FileNotFoundException>(opener.OpenSettingsFile);
        Assert.Contains("appsettings.Example.json", ex.Message);
    }

    private sealed class FakeEnv(string root) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = root;
        public string WebRootPath { get; set; } = root;
        public string ApplicationName { get; set; } = "t";
        public string EnvironmentName { get; set; } = "Test";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    }
}
