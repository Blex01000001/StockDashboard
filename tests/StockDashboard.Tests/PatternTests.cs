using StockDashboard.Models;
using StockDashboard.Models.Patterns;
using StockDashboard.Services.Patterns;
using StockDashboard.Services;

namespace StockDashboard.Tests;

public class BullishEngulfingDetectorTests
{
    private static StockDataPoint K(double open, double close, double? high = null, double? low = null, long volume = 1000) => new()
    {
        Open = open, Close = close,
        High = high ?? Math.Max(open, close), Low = low ?? Math.Min(open, close), Volume = volume
    };

    // 關閉「先下跌」，只測吞沒本身
    private static BullishEngulfingOptions NoTrend() => new() { RequireDowntrend = false };

    // 前一天黑 K 100→98 (實體 2%)，當天紅 K 97→101 吞沒
    private static List<StockDataPoint> Basic() => new() { K(100, 98), K(97, 101) };

    [Fact]
    public void 標準看漲吞沒成立() => Assert.True(BullishEngulfingDetector.IsMatch(Basic(), 1, NoTrend()));

    [Fact]
    public void 第一天無法判斷() => Assert.False(BullishEngulfingDetector.IsMatch(Basic(), 0, NoTrend()));

    [Fact]
    public void 前一天不是黑K不成立() => Assert.False(BullishEngulfingDetector.IsMatch(new List<StockDataPoint> { K(98, 100), K(97, 101) }, 1, NoTrend()));

    [Fact]
    public void 當天不是紅K不成立() => Assert.False(BullishEngulfingDetector.IsMatch(new List<StockDataPoint> { K(100, 98), K(101, 97) }, 1, NoTrend()));

    [Fact]
    public void 收盤沒超過前一天開盤不成立() => Assert.False(BullishEngulfingDetector.IsMatch(new List<StockDataPoint> { K(100, 98), K(97, 99.9) }, 1, NoTrend()));

    [Fact]
    public void 開盤高於前一天收盤不成立() => Assert.False(BullishEngulfingDetector.IsMatch(new List<StockDataPoint> { K(100, 98), K(98.5, 101) }, 1, NoTrend()));

    [Fact]
    public void 容許誤差內也算吞沒()
    {
        var candles = new List<StockDataPoint> { K(100, 98), K(98.5, 101) };   // 開盤高前一天收盤約 0.51%
        var options = NoTrend();
        options.TolerancePct = 0.6;
        Assert.True(BullishEngulfingDetector.IsMatch(candles, 1, options));
    }

    [Fact]
    public void 開平與收平剛好等於前一天實體也成立() =>
        Assert.True(BullishEngulfingDetector.IsMatch(new List<StockDataPoint> { K(100, 98), K(98, 100) }, 1, NoTrend()));

    [Fact]
    public void 前一天實體太小不成立()
    {
        var candles = new List<StockDataPoint> { K(100, 99.8), K(99.7, 100.5) };   // 前一天實體 0.2%
        Assert.False(BullishEngulfingDetector.IsMatch(candles, 1, NoTrend()));
        var options = NoTrend();
        options.MinPrevBodyPct = 0.1;
        Assert.True(BullishEngulfingDetector.IsMatch(candles, 1, options));
    }

    [Fact]
    public void 實體倍數不足不成立()
    {
        var options = NoTrend();
        options.MinBodyRatio = 2.5;      // 當天實體 4 / 前一天實體 2 = 2 倍
        Assert.False(BullishEngulfingDetector.IsMatch(Basic(), 1, options));
        options.MinBodyRatio = 2;
        Assert.True(BullishEngulfingDetector.IsMatch(Basic(), 1, options));
    }

    [Fact]
    public void 要求影線吞沒時_前一天影線較長不成立()
    {
        var candles = new List<StockDataPoint> { K(100, 98, high: 103, low: 96), K(97, 101, high: 102, low: 96.5) };
        Assert.True(BullishEngulfingDetector.IsMatch(candles, 1, NoTrend()));
        var options = NoTrend();
        options.IncludeShadows = true;
        Assert.False(BullishEngulfingDetector.IsMatch(candles, 1, options));
    }

    [Fact]
    public void 要求先下跌_跌幅足夠才成立()
    {
        // 收盤：110, 108, 106, 104, 102, 100(前一天黑K 102→100), 當天 99→103
        var candles = new List<StockDataPoint> { K(110, 110), K(110, 108), K(108, 106), K(106, 104), K(104, 102), K(102, 100), K(99, 103) };
        var options = new BullishEngulfingOptions { DowntrendDays = 5, DowntrendPct = 3 };   // 前一天 100 相對 5 天前 110 跌 9%
        Assert.True(BullishEngulfingDetector.IsMatch(candles, 6, options));
        options.DowntrendPct = 10;
        Assert.False(BullishEngulfingDetector.IsMatch(candles, 6, options));
    }

    [Fact]
    public void 要求先下跌_歷史天數不足不成立() =>
        Assert.False(BullishEngulfingDetector.IsMatch(Basic(), 1, new BullishEngulfingOptions()));

    [Fact]
    public void 要求量增_成交量不足不成立()
    {
        var candles = new List<StockDataPoint> { K(100, 100, volume: 1000), K(100, 100, volume: 1000), K(100, 98, volume: 1000), K(97, 101, volume: 1400) };
        var options = NoTrend();
        options.RequireVolume = true;
        options.VolumeAvgDays = 3;
        options.VolumeMultiplier = 1.5;
        Assert.False(BullishEngulfingDetector.IsMatch(candles, 3, options));
        candles[3] = K(97, 101, volume: 1500);
        Assert.True(BullishEngulfingDetector.IsMatch(candles, 3, options));
    }
}

public class PatternScanServiceTests
{
    // 產生 n 根平盤 K 棒 (收盤 = close)，再於 signalIndex - 1 / signalIndex 放入看漲吞沒
    private static List<StockDataPoint> Series(int n, params int[] signals)
    {
        var list = new List<StockDataPoint>();
        for (int i = 0; i < n; i++)
        {
            double c = 100 + i;
            list.Add(new StockDataPoint { Date = $"2026-{1 + i / 28:00}-{1 + i % 28:00}", Open = c, High = c, Low = c, Close = c, Volume = 1000 });
        }
        foreach (int s in signals)
        {
            var p = list[s - 1]; p.Open = p.Close + 2; p.High = p.Open;               // 前一天黑 K
            var t = list[s]; t.Open = p.Close - 1; t.Close = p.Open + 1; t.High = t.Close; t.Low = t.Open;   // 當天紅 K 吞沒
        }
        return list;
    }

    private static readonly BullishEngulfingOptions NoTrend = new() { RequireDowntrend = false };

    [Fact]
    public void 小卡包含前10天與後20天()
    {
        var candles = Series(60, 30);

        var m = Assert.Single(PatternScanService.FindMatches("2330", candles, NoTrend, 10, 20));

        Assert.Equal(31, m.Candles.Count);
        Assert.Equal(10, m.SignalIndex);
        Assert.Equal(candles[30].Date, m.SignalDate);
        Assert.Equal(candles[20].Date, m.Candles[0].Date);
        Assert.Equal(candles[50].Date, m.Candles[^1].Date);
    }

    [Fact]
    public void 靠近資料開頭與結尾時小卡較短_之後報酬為Null()
    {
        var candles = Series(40, 3, 35);

        var matches = PatternScanService.FindMatches("2330", candles, NoTrend, 10, 20);

        var early = matches.Single(m => m.SignalDate == candles[3].Date);
        Assert.Equal(3, early.SignalIndex);
        Assert.Equal(24, early.Candles.Count);       // 前 3 + 當天 + 後 20

        var late = matches.Single(m => m.SignalDate == candles[35].Date);
        Assert.Equal(15, late.Candles.Count);        // 前 10 + 當天 + 後 4
        Assert.Null(late.Return5);
        Assert.Null(late.Return20);
    }

    [Fact]
    public void 之後報酬以型態當天收盤為基準()
    {
        var candles = Series(60, 30);

        var m = Assert.Single(PatternScanService.FindMatches("2330", candles, NoTrend, 10, 20));

        double baseClose = candles[30].Close;
        Assert.Equal(Math.Round((candles[35].Close - baseClose) / baseClose * 100, 2), m.Return5);
        Assert.Equal(Math.Round((candles[50].Close - baseClose) / baseClose * 100, 2), m.Return20);
    }

    [Fact]
    public void 均線以完整歷史計算()
    {
        var candles = Series(60, 30);

        var m = Assert.Single(PatternScanService.FindMatches("2330", candles, NoTrend, 10, 20));

        // 小卡第一根是第 20 根：MA5 = 第 16~20 根收盤平均，MA20 = 第 1~20 根收盤平均
        Assert.Equal(Math.Round(candles.Skip(16).Take(5).Average(c => c.Close), 2), m.Candles[0].Ma5);
        Assert.Equal(Math.Round(candles.Skip(1).Take(20).Average(c => c.Close), 2), m.Candles[0].Ma20);
    }

    [Fact]
    public void 均線天數不足時為Null()
    {
        var candles = Series(40, 3);

        var m = Assert.Single(PatternScanService.FindMatches("2330", candles, NoTrend, 10, 20));

        Assert.Null(m.Candles[0].Ma5);
        Assert.Null(m.Candles[0].Ma20);
        Assert.NotNull(m.Candles[4].Ma5);   // 第 5 根開始有 MA5
    }
}
