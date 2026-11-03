using StockDashboard.Models;
using StockDashboard.Models.Entities;
using StockDashboard.Models.Patterns;
using StockDashboard.Services;

namespace StockDashboard.Tests;

public class StockChartServiceTests
{
    private static List<StockDataPoint> Candles(int count) => Enumerable.Range(0, count).Select(i =>
    {
        double close = 100 + 10 * Math.Sin(i / 7.0);
        return new StockDataPoint { Date = new DateTime(2025, 1, 1).AddDays(i).ToString("yyyy-MM-dd"), Open = close - 1, High = close + 2, Low = close - 2, Close = close, Volume = 1000 + i };
    }).ToList();

    [Fact]
    public void 只畫最近N根_但均線用完整歷史計算()
    {
        var full = Candles(400);
        var data = StockChartService.Build(full, 220, new IndicatorSettings(), Array.Empty<InstitutionalDailyEntity>());

        Assert.Equal(180, data.WindowStart);
        Assert.Equal(220, data.Candles.Count);
        Assert.Equal(full[180].Date, data.Candles[0].Date);
        Assert.Equal(full[^1].Date, data.Candles[^1].Date);
        // 圖最左邊那根已有 120 天以上歷史，MA120 不是 null，且等於完整歷史算出的值
        var expected = Math.Round(full.Skip(180 - 119).Take(120).Average(c => c.Close), 2);
        Assert.Equal(expected, data.Candles[0].Ma120);
        Assert.NotNull(data.Candles[0].Dif);
        Assert.NotNull(data.Candles[0].K);
    }

    [Fact]
    public void 資料不足N根_全部顯示()
    {
        var data = StockChartService.Build(Candles(50), 220, new IndicatorSettings(), Array.Empty<InstitutionalDailyEntity>());
        Assert.Equal(0, data.WindowStart);
        Assert.Equal(50, data.Candles.Count);
        Assert.Null(data.Candles[0].Ma5);
    }

    [Fact]
    public void 三大法人依日期填入_調整參數後重算指標()
    {
        var full = Candles(100);
        var inst = new[] { new InstitutionalDailyEntity { StockId = "2330", TradeDate = full[^1].Date, ForeignNet = 3000, TrustNet = -1000, DealerNet = 0, TotalNet = 2000, ForeignHoldingPct = 70 } };
        var data = StockChartService.Build(full, 30, new IndicatorSettings(), inst);

        Assert.Equal(2000, data.Candles[^1].TotalNet);
        Assert.Equal(70, data.Candles[^1].ForeignHoldingPct);
        Assert.Null(data.Candles[^2].TotalNet);

        double? before = data.Candles[^1].RsiFast;
        StockChartService.Recompute(data, new IndicatorSettings { RsiFast = 3 });
        Assert.NotEqual(before, data.Candles[^1].RsiFast);
        Assert.Equal(2000, data.Candles[^1].TotalNet);   // 重算指標不影響三大法人
    }

    [Fact]
    public void 改顯示筆數_重切範圍_0為全部()
    {
        var full = Candles(400);
        var inst = new[] { new InstitutionalDailyEntity { StockId = "2330", TradeDate = full[^1].Date, TotalNet = 5 } };
        var data = StockChartService.Build(full, 120, new IndicatorSettings(), inst);
        Assert.Equal(120, data.Candles.Count);

        StockChartService.SetWindow(data, 0, new IndicatorSettings());
        Assert.Equal(0, data.WindowStart);
        Assert.Equal(400, data.Candles.Count);
        Assert.Equal(5, data.Candles[^1].TotalNet);     // 三大法人重新填入
        Assert.NotNull(data.Candles[^1].Ma120);
    }

    [Theory]
    [InlineData(370, 250, 250)]   // 倒數第 30 根：一年就看得到 (30 + 前面多留 30 根 ≤ 250)
    [InlineData(200, 250, 250)]   // 倒數第 200 根：200 + 30 ≤ 250
    [InlineData(100, 250, 750)]   // 倒數第 300 根：一年不夠，改三年
    [InlineData(100, 120, 750)]   // 原本半年也一樣，挑最小夠用的
    [InlineData(5, 250, 750)]     // 從第 5 根開始要 425 根：三年夠用
    [InlineData(100, 0, 0)]       // 原本就是全部
    public void 標示日期不在範圍內_自動加大筆數(int focusIndex, int preferred, int expected)
    {
        var full = Candles(400);
        var options = new[] { 120, 250, 750, 0 };
        int count = StockChartService.CountToInclude(full, full[focusIndex].Date, options, preferred);
        Assert.Equal(expected, count);
    }

    [Fact]
    public void 比三年還舊_改全部()
    {
        var full = Candles(1000);
        Assert.Equal(0, StockChartService.CountToInclude(full, full[50].Date, new[] { 120, 250, 750, 0 }, 250));
    }
}
