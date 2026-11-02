using System.Net;
using Microsoft.Extensions.Configuration;
using StockDashboard.Services;

namespace StockDashboard.Tests;

public class TwseApiClientTests
{
    private static TwseApiClient Client(FakeHttpHandler handler, IConfiguration? config = null) =>
        new(new HttpClient(handler), config);

    [Fact]
    public async Task GetMonthlyPrices_解析證交所實際回應()
    {
        var handler = FakeHttpHandler.Always(Fixtures.Read("stock_day_2330_202610.json"));

        var rows = await Client(handler).GetMonthlyPricesAsync("2330", new DateTime(2026, 10, 15));

        Assert.Equal(19, rows.Count);
        var first = rows[0];
        Assert.Equal("2330", first.StockId);
        Assert.Equal("2026-10-01", first.TradeDate);          // 民國 115 → 西元 2026
        Assert.Equal(20_666_092, first.Volume);               // 去除千分號
        Assert.Equal(51_702_445_610, first.Amount);
        Assert.Equal(2490, first.OpenPrice);
        Assert.Equal(2510, first.HighPrice);
        Assert.Equal(2485, first.LowPrice);
        Assert.Equal(2510, first.ClosePrice);
        Assert.Equal(30, first.PriceChange);                  // "+30.00"
        Assert.Equal(57_634, first.TradeCount);
        Assert.Equal(0, rows.Single(r => r.TradeDate == "2026-10-07").PriceChange);   // " 0.00"
        Assert.Equal(-50, rows.Single(r => r.TradeDate == "2026-10-15").PriceChange);
    }

    [Fact]
    public async Task GetMonthlyPrices_請求網址使用月份第一天()
    {
        var handler = FakeHttpHandler.Always(Fixtures.Read("stock_day_2330_202610.json"));

        await Client(handler).GetMonthlyPricesAsync("2330", new DateTime(2026, 10, 15));

        var url = handler.Requests.Single().ToString();
        Assert.StartsWith(TwseApiClient.DefaultStockDayUrl, url);
        Assert.Contains("date=20261001", url);
        Assert.Contains("stockNo=2330", url);
    }

    [Fact]
    public async Task GetMonthlyPrices_網址可由設定覆寫()
    {
        var handler = FakeHttpHandler.Always("""{"stat":"很抱歉，沒有符合條件的資料!"}""");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Twse:StockDayUrl"] = "http://127.0.0.1:9999/STOCK_DAY" })
            .Build();

        await Client(handler, config).GetMonthlyPricesAsync("2330", new DateTime(2026, 10, 1));

        Assert.StartsWith("http://127.0.0.1:9999/STOCK_DAY?", handler.Requests.Single().ToString());
    }

    [Fact]
    public async Task GetMonthlyPrices_無交易資料回傳空清單()
    {
        var handler = FakeHttpHandler.Always("""{"stat":"很抱歉，沒有符合條件的資料!"}""");

        var rows = await Client(handler).GetMonthlyPricesAsync("2330", new DateTime(2026, 11, 1));

        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetMonthlyPrices_無法解析的數值存為Null_格式錯誤的日期略過()
    {
        var handler = FakeHttpHandler.Always("""
            {"stat":"OK","data":[
              ["115/10/01","1,000","2,000","--","--","--","--","X0.00","5",""],
              ["壞掉的日期","1","1","1","1","1","1","+1.00","1",""],
              ["115/10/02","1"]
            ]}
            """);

        var rows = await Client(handler).GetMonthlyPricesAsync("2330", new DateTime(2026, 10, 1));

        var row = Assert.Single(rows);
        Assert.Equal(1000, row.Volume);
        Assert.Null(row.OpenPrice);      // "--" (當日無成交)
        Assert.Null(row.ClosePrice);
        Assert.Null(row.PriceChange);    // "X0.00" (除權息不比價)
    }

    [Fact]
    public async Task GetMonthlyPrices_回傳HTML時拋出易懂的錯誤()
    {
        var handler = FakeHttpHandler.Always("<html><body>系統忙碌</body></html>", "text/html");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Client(handler).GetMonthlyPricesAsync("2330", new DateTime(2026, 10, 1)));

        Assert.Contains("不是 JSON", ex.Message);
        Assert.Contains("<html>", ex.Message);
    }

    [Fact]
    public async Task GetMonthlyPrices_HTTP403時提示可能被封鎖()
    {
        var handler = FakeHttpHandler.Always("", "text/plain", HttpStatusCode.Forbidden);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => Client(handler).GetMonthlyPricesAsync("2330", new DateTime(2026, 10, 1)));

        Assert.Contains("403", ex.Message);
        Assert.Contains("封鎖", ex.Message);
    }

    [Fact]
    public async Task GetListedStocks_去除空白與重複代號()
    {
        var handler = FakeHttpHandler.Always(Fixtures.Read("stock_day_all.json"));

        var stocks = await Client(handler).GetListedStocksAsync();

        Assert.Equal(new[] { "2330", "0050", "00981A" }, stocks.Select(s => s.StockId));
        Assert.Equal("台積電", stocks[0].StockName);           // 重複代號取第一筆
        Assert.Equal("元大台灣50", stocks[1].StockName);        // 去除尾端空白
        Assert.All(stocks, s => Assert.Equal("TWSE", s.Market));
        Assert.StartsWith(TwseApiClient.DefaultStockDayAllUrl, handler.Requests.Single().ToString());
    }
}
