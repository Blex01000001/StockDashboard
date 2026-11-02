using StockDashboard.Models;
using StockDashboard.Services;
using StockDashboard.Services.Patterns;

namespace StockDashboard.Tests;

public class TechnicalIndicatorsTests
{
    [Fact]
    public void 移動平均_前N減1筆為Null()
    {
        var ma = TechnicalIndicators.MovingAverage(new double[] { 1, 2, 3, 4, 5 }, 3);
        Assert.Equal(new double?[] { null, null, 2, 3, 4 }, ma);
    }

    [Fact]
    public void 移動平均可指定小數位數()
    {
        var ma = TechnicalIndicators.MovingAverage(new double[] { 1000, 1001, 1003 }, 3, decimals: 0);
        Assert.Equal(1001, ma[2]);
    }

    [Fact]
    public void MACD_手算驗證()
    {
        // 第 2 天：EMA12 = 10 + 1 × 2/13 = 10.153846；EMA26 = 10 + 1 × 2/27 = 10.074074
        // DIF = 0.079772；MACD = 0 + (0.079772 − 0) × 2/10 = 0.015954；OSC = 0.063818
        var (dif, macd, osc) = TechnicalIndicators.Macd(new double[] { 10, 11 });
        Assert.Equal(new double?[] { 0, 0.080 }, dif);
        Assert.Equal(new double?[] { 0, 0.016 }, macd);
        Assert.Equal(new double?[] { 0, 0.064 }, osc);
    }

    [Fact]
    public void MACD_股價不變時為0_持續上漲時DIF與MACD為正()
    {
        var flat = TechnicalIndicators.Macd(Enumerable.Repeat(100.0, 50).ToList());
        Assert.All(flat.Dif, v => Assert.Equal(0, v));

        var rising = TechnicalIndicators.Macd(Enumerable.Range(0, 60).Select(i => 100.0 + i).ToList());
        Assert.True(rising.Dif[^1] > 0);
        Assert.True(rising.Signal[^1] > 0);
        // 等速上漲時 DIF 逐漸收斂，訊號線追上來，OSC 趨近 0 但仍為正
        Assert.True(rising.Histogram[^1] >= 0);
    }

    [Fact]
    public void MACD_空序列()
    {
        var (dif, _, _) = TechnicalIndicators.Macd(Array.Empty<double>());
        Assert.Empty(dif);
    }

    [Fact]
    public void 小卡K棒帶有成交量均線與MACD()
    {
        // 成交量 1000, 1100, 1200 … 遞增；收盤固定 100，看漲吞沒放在第 30 根
        var candles = new List<StockDataPoint>();
        for (int i = 0; i < 60; i++)
            candles.Add(new StockDataPoint { Date = $"d{i:000}", Open = 100, High = 100, Low = 100, Close = 100, Volume = 1000 + 100 * i });
        candles[29].Open = 102; candles[29].High = 102;
        candles[30].Open = 99; candles[30].Close = 103; candles[30].High = 103; candles[30].Low = 99;

        var engulfing = new BullishEngulfingPattern();
        var values = Models.Patterns.PatternParameterValues.FromDefaults(engulfing.Parameters).Set(PatternFilters.RequireDowntrend, false);
        var m = Assert.Single(PatternScanService.FindMatches("X", candles, engulfing, values, 10, 20));

        var c = m.Candles[0];   // 第 20 根
        Assert.Equal(Math.Round(candles.Skip(16).Take(5).Average(x => (double)x.Volume)), c.VolMa5);
        Assert.Equal(Math.Round(candles.Skip(11).Take(10).Average(x => (double)x.Volume)), c.VolMa10);

        var (dif, macd, osc) = TechnicalIndicators.Macd(candles.Select(x => x.Close).ToList());
        Assert.Equal((dif[20], macd[20], osc[20]), (c.Dif, c.Macd, c.Osc));
        Assert.True(m.Candles[m.SignalIndex].Dif > 0);   // 型態當天大漲，DIF 轉正
    }
}
