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
    public void KD_手算驗證()
    {
        // 週期 3：第 3 根 RSV = (11.5 − 8) / (12 − 8) × 100 = 87.5；K = (50×2 + 87.5)/3 = 62.5；D = (50×2 + 62.5)/3 = 54.17
        //         第 4 根 RSV = (11 − 9) / (12 − 9) × 100 = 66.67；K = (62.5×2 + 66.67)/3 = 63.89；D = (54.17×2 + 63.89)/3 = 57.41
        var (k, d) = TechnicalIndicators.Kd(new double[] { 10, 11, 12, 12 }, new double[] { 8, 9, 10, 11 }, new double[] { 9, 10, 11.5, 11 }, period: 3);
        Assert.Equal(new double?[] { null, null, 62.5, 63.89 }, k);
        Assert.Equal(new double?[] { null, null, 54.17, 57.41 }, d);
    }

    [Fact]
    public void KD_最高等於最低時RSV視為50()
    {
        var flat = Enumerable.Repeat(100.0, 20).ToList();
        var (k, d) = TechnicalIndicators.Kd(flat, flat, flat);
        Assert.Equal(50, k[^1]);
        Assert.Equal(50, d[^1]);
    }

    [Fact]
    public void RSI_手算驗證()
    {
        // 週期 2，漲跌 +1, −0.5, +1.5：第 3 根 平均漲 0.5 / 平均跌 0.25 → RSI 66.67；
        // 第 4 根 平均漲 (0.5 + 1.5)/2 = 1、平均跌 (0.25 + 0)/2 = 0.125 → RSI 88.89
        Assert.Equal(new double?[] { null, null, 66.67, 88.89 }, TechnicalIndicators.Rsi(new double[] { 10, 11, 10.5, 12 }, 2));
    }

    [Fact]
    public void RSI_只漲為100_不動為50_資料不足全為Null()
    {
        Assert.Equal(100, TechnicalIndicators.Rsi(Enumerable.Range(0, 20).Select(i => 100.0 + i).ToList(), 6)[^1]);
        Assert.Equal(50, TechnicalIndicators.Rsi(Enumerable.Repeat(100.0, 20).ToList(), 6)[^1]);
        Assert.All(TechnicalIndicators.Rsi(new double[] { 1, 2, 3 }, 6), v => Assert.Null(v));
    }

    [Theory]
    [InlineData(12, 26, 9, null)]
    [InlineData(6, 13, 5, null)]
    [InlineData(26, 12, 9, "快線天數必須小於慢線天數")]
    [InlineData(12, 12, 9, "快線天數必須小於慢線天數")]
    [InlineData(0, 26, 9, "需介於")]
    [InlineData(12, 26, 0, "需介於")]
    [InlineData(12, 500, 9, "需介於")]
    public void MACD參數檢查(int fast, int slow, int signal, string? error)
    {
        var message = new Models.Patterns.IndicatorSettings { MacdFast = fast, MacdSlow = slow, MacdSignal = signal }.ValidateMacd();
        if (error == null) Assert.Null(message);
        else Assert.Contains(error, message);
    }

    [Theory]
    [InlineData("kd", 9, 3, 3, 6, 12, null)]
    [InlineData("kd", 0, 3, 3, 6, 12, "KD 參數需介於")]
    [InlineData("kd", 9, 3, 300, 6, 12, "KD 參數需介於")]
    [InlineData("rsi", 9, 3, 3, 5, 10, null)]
    [InlineData("rsi", 9, 3, 3, 12, 6, "短週期必須小於長週期")]
    [InlineData("rsi", 9, 3, 3, 0, 12, "RSI 參數需介於")]
    [InlineData("volume", 0, 0, 0, 0, 0, null)]   // 成交量沒有參數
    public void KD與RSI參數檢查(string subChart, int kdPeriod, int k, int d, int rsiFast, int rsiSlow, string? error)
    {
        var settings = new Models.Patterns.IndicatorSettings { KdPeriod = kdPeriod, KdKSmooth = k, KdDSmooth = d, RsiFast = rsiFast, RsiSlow = rsiSlow };
        var message = settings.Validate(subChart);
        if (error == null) Assert.Null(message);
        else Assert.Contains(error, message);
    }

    [Fact]
    public void 附圖依固定順序疊加()
    {
        var state = new ChartDisplaySettings();
        Assert.Equal(new[] { "volume" }, state.OrderedSubCharts);          // 預設只有成交量
        Assert.Equal(new[] { "MA5", "MA10", "MA20", "MA60" }, state.VisibleMaNames);
        state.SubCharts.Add("rsi");
        state.SubCharts.Add("macd");
        state.SubCharts.Remove("volume");
        Assert.Equal(new[] { "macd", "rsi" }, state.OrderedSubCharts);    // 不管勾選先後，依 成交量→MACD→KD→RSI 排列
    }

    [Fact]
    public void 調整KD與RSI參數後重算()
    {
        var candles = Enumerable.Range(0, 80).Select(i =>
        {
            double c = 100 + Math.Sin(i / 4.0) * 5;
            return new StockDataPoint { Date = $"d{i:000}", Open = c - 0.5, Close = c, High = c + 1, Low = c - 1.5, Volume = 1000 };
        }).ToList();
        var match = new Models.Patterns.PatternMatch
        {
            StockId = "X", WindowStart = 40,
            Candles = Enumerable.Range(0, 10).Select(_ => new Models.Patterns.PatternCandle()).ToList()
        };
        var result = new Models.Patterns.PatternScanResult { Matches = { match }, FullCandles = { ["X"] = candles } };

        var settings = new Models.Patterns.IndicatorSettings { KdPeriod = 5, RsiFast = 3, RsiSlow = 9 };
        PatternScanService.RecomputeIndicators(result, settings);

        var closes = candles.Select(c => c.Close).ToList();
        var (k, d) = TechnicalIndicators.Kd(candles.Select(c => c.High).ToList(), candles.Select(c => c.Low).ToList(), closes, 5);
        Assert.Equal(k[45], match.Candles[5].K);
        Assert.Equal(d[45], match.Candles[5].D);
        Assert.Equal(TechnicalIndicators.Rsi(closes, 3)[45], match.Candles[5].RsiFast);
        Assert.Equal(TechnicalIndicators.Rsi(closes, 9)[45], match.Candles[5].RsiSlow);
    }

    [Fact]
    public void 調整MACD參數後重算_與直接計算一致()
    {
        var candles = new List<StockDataPoint>();
        var rng = new Random(7);
        double price = 100;
        for (int i = 0; i < 120; i++)
        {
            double open = price;
            price *= 1 + (rng.NextDouble() - 0.5) * 0.04;
            candles.Add(new StockDataPoint { Date = $"d{i:000}", Open = open, Close = price, High = Math.Max(open, price) * 1.01, Low = Math.Min(open, price) * 0.99, Volume = 1000 });
        }
        var engulfing = new BullishEngulfingPattern();
        var values = Models.Patterns.PatternParameterValues.FromDefaults(engulfing.Parameters).Set(PatternFilters.RequireDowntrend, false);
        var matches = PatternScanService.FindMatches("X", candles, engulfing, values, 10, 20);
        Assert.NotEmpty(matches);

        var result = new Models.Patterns.PatternScanResult { Matches = matches, FullCandles = { ["X"] = candles } };
        var before = matches[0].Candles[^1].Dif;
        var settings = new Models.Patterns.IndicatorSettings { MacdFast = 6, MacdSlow = 13, MacdSignal = 5 };
        PatternScanService.RecomputeIndicators(result, settings);

        var (dif, macd, _) = TechnicalIndicators.Macd(candles.Select(c => c.Close).ToList(), 6, 13, 5);
        var m = matches[0];
        int last = m.WindowStart + m.Candles.Count - 1;
        Assert.Equal(dif[last], m.Candles[^1].Dif);
        Assert.Equal(macd[last], m.Candles[^1].Macd);
        Assert.NotEqual(before, m.Candles[^1].Dif);
        // KD、RSI 也已填入
        Assert.NotNull(m.Candles[^1].K);
        Assert.NotNull(m.Candles[^1].RsiFast);
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

    [Fact]
    public void 布林通道_上下軌為均線加減母體標準差()
    {
        // 收盤 2, 4, 4, 4, 5, 5, 7, 9：前 8 筆平均 5、母體標準差 2
        var closes = new double[] { 2, 4, 4, 4, 5, 5, 7, 9, 5 };
        var (upper, lower) = TechnicalIndicators.Bollinger(closes, 8, 2);

        for (int i = 0; i < 7; i++) { Assert.Null(upper[i]); Assert.Null(lower[i]); }   // 天數不足
        Assert.Equal(9.0, upper[7]);    // 5 + 2 × 2
        Assert.Equal(1.0, lower[7]);    // 5 − 2 × 2
        Assert.Equal(7.0, TechnicalIndicators.Bollinger(closes, 8, 1).Upper[7]);   // 倍數 1
    }

    [Fact]
    public void 布林通道_收盤不變時上下軌等於均線()
    {
        var (upper, lower) = TechnicalIndicators.Bollinger(Enumerable.Repeat(50.0, 30).ToList());
        Assert.Equal(50.0, upper[29]);
        Assert.Equal(50.0, lower[29]);
    }

    [Theory]
    [InlineData(20, 2, null)]
    [InlineData(0, 2, "天數需介於")]
    [InlineData(300, 2, "天數需介於")]
    [InlineData(20, 0, "倍數需介於")]
    [InlineData(20, 11, "倍數需介於")]
    public void 布林通道參數檢查(int period, int multiplier, string? error)
    {
        var message = new Models.Patterns.IndicatorSettings { BollPeriod = period, BollMultiplier = multiplier }.Validate("boll");
        if (error == null) Assert.Null(message);
        else Assert.Contains(error, message);
    }

    [Fact]
    public void 布林通道_只在開啟時填入K棒_且以完整歷史計算()
    {
        var candles = Enumerable.Range(0, 60).Select(i =>
        {
            double c = 100 + Math.Sin(i / 3.0) * 5;
            return new StockDataPoint { Date = $"d{i:000}", Open = c, Close = c, High = c, Low = c, Volume = 1000 };
        }).ToList();
        List<Models.Patterns.PatternCandle> Window() => Enumerable.Range(0, 10).Select(_ => new Models.Patterns.PatternCandle()).ToList();

        // 預設關閉：不計算
        var off = Window();
        PatternScanService.ApplyIndicators(new[] { (40, (IReadOnlyList<Models.Patterns.PatternCandle>)off) }, candles, new Models.Patterns.IndicatorSettings());
        Assert.All(off, c => { Assert.Null(c.BollUpper); Assert.Null(c.BollLower); });

        // 開啟：與直接計算一致 (第一根在完整歷史的第 40 位，不是從小卡範圍重算)
        var on = Window();
        PatternScanService.ApplyIndicators(new[] { (40, (IReadOnlyList<Models.Patterns.PatternCandle>)on) }, candles,
            new Models.Patterns.IndicatorSettings { Bollinger = true });
        var (upper, lower) = TechnicalIndicators.Bollinger(candles.Select(c => c.Close).ToList(), 20, 2);
        Assert.Equal(upper[40], on[0].BollUpper);
        Assert.Equal(lower[49], on[9].BollLower);
        Assert.NotNull(on[0].BollUpper);
    }
}
