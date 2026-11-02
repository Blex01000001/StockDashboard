using StockDashboard.Models;
using StockDashboard.Models.Patterns;
using StockDashboard.Services;
using StockDashboard.Services.Patterns;

namespace StockDashboard.Tests;

internal static class K
{
    public static StockDataPoint Bar(double open, double close, double? high = null, double? low = null, long volume = 1000) => new()
    {
        Open = open, Close = close,
        High = high ?? Math.Max(open, close), Low = low ?? Math.Min(open, close), Volume = volume
    };

    // 預設參數，並關閉「先下跌」以便只測型態本身
    public static PatternParameterValues NoTrend(ICandlePattern pattern) =>
        PatternParameterValues.FromDefaults(pattern.Parameters).Set(PatternFilters.RequireDowntrend, false);
}

public class BullishEngulfingPatternTests
{
    private static readonly BullishEngulfingPattern P = new();
    private static PatternParameterValues V() => K.NoTrend(P);

    // 前一天黑 K 100→98 (實體 2%)，當天紅 K 97→101 吞沒
    private static List<StockDataPoint> Basic() => new() { K.Bar(100, 98), K.Bar(97, 101) };

    [Fact] public void 標準看漲吞沒成立() => Assert.True(P.IsMatch(Basic(), 1, V()));
    [Fact] public void 第一天無法判斷() => Assert.False(P.IsMatch(Basic(), 0, V()));
    [Fact] public void 前一天不是黑K不成立() => Assert.False(P.IsMatch(new List<StockDataPoint> { K.Bar(98, 100), K.Bar(97, 101) }, 1, V()));
    [Fact] public void 當天不是紅K不成立() => Assert.False(P.IsMatch(new List<StockDataPoint> { K.Bar(100, 98), K.Bar(101, 97) }, 1, V()));
    [Fact] public void 收盤沒超過前一天開盤不成立() => Assert.False(P.IsMatch(new List<StockDataPoint> { K.Bar(100, 98), K.Bar(97, 99.9) }, 1, V()));
    [Fact] public void 開盤高於前一天收盤不成立() => Assert.False(P.IsMatch(new List<StockDataPoint> { K.Bar(100, 98), K.Bar(98.5, 101) }, 1, V()));
    [Fact] public void 開平與收平剛好等於前一天實體也成立() => Assert.True(P.IsMatch(new List<StockDataPoint> { K.Bar(100, 98), K.Bar(98, 100) }, 1, V()));

    [Fact]
    public void 容許誤差內也算吞沒() =>
        Assert.True(P.IsMatch(new List<StockDataPoint> { K.Bar(100, 98), K.Bar(98.5, 101) }, 1, V().Set(BullishEngulfingPattern.TolerancePct, 0.6)));

    [Fact]
    public void 前一天實體太小不成立()
    {
        var candles = new List<StockDataPoint> { K.Bar(100, 99.8), K.Bar(99.7, 100.5) };   // 前一天實體 0.2%
        Assert.False(P.IsMatch(candles, 1, V()));
        Assert.True(P.IsMatch(candles, 1, V().Set(BullishEngulfingPattern.MinPrevBodyPct, 0.1)));
    }

    [Fact]
    public void 實體倍數不足不成立()
    {
        Assert.False(P.IsMatch(Basic(), 1, V().Set(BullishEngulfingPattern.MinBodyRatio, 2.5)));   // 實際 2 倍
        Assert.True(P.IsMatch(Basic(), 1, V().Set(BullishEngulfingPattern.MinBodyRatio, 2)));
    }

    [Fact]
    public void 要求影線吞沒時_前一天影線較長不成立()
    {
        var candles = new List<StockDataPoint> { K.Bar(100, 98, high: 103, low: 96), K.Bar(97, 101, high: 102, low: 96.5) };
        Assert.True(P.IsMatch(candles, 1, V()));
        Assert.False(P.IsMatch(candles, 1, V().Set(BullishEngulfingPattern.IncludeShadows, true)));
    }

    [Fact]
    public void 要求先下跌_跌幅足夠才成立()
    {
        // 收盤：110, 108, 106, 104, 102, 100(前一天黑K 102→100), 當天 99→103；100 相對 5 天前 110 跌 9%
        var candles = new List<StockDataPoint> { K.Bar(110, 110), K.Bar(110, 108), K.Bar(108, 106), K.Bar(106, 104), K.Bar(104, 102), K.Bar(102, 100), K.Bar(99, 103) };
        var values = PatternParameterValues.FromDefaults(P.Parameters);
        Assert.True(P.IsMatch(candles, 6, values));
        Assert.False(P.IsMatch(candles, 6, values.Set(PatternFilters.DowntrendPct, 10)));
    }

    [Fact]
    public void 要求先下跌_歷史天數不足不成立() =>
        Assert.False(P.IsMatch(Basic(), 1, PatternParameterValues.FromDefaults(P.Parameters)));

    [Fact]
    public void 要求量增_成交量不足不成立()
    {
        var candles = new List<StockDataPoint> { K.Bar(100, 100), K.Bar(100, 100), K.Bar(100, 98), K.Bar(97, 101, volume: 1400) };
        var values = V().Set(PatternFilters.RequireVolume, true).Set(PatternFilters.VolumeAvgDays, 3).Set(PatternFilters.VolumeMultiplier, 1.5);
        Assert.False(P.IsMatch(candles, 3, values));
        candles[3] = K.Bar(97, 101, volume: 1500);
        Assert.True(P.IsMatch(candles, 3, values));
    }
}

public class MorningStarPatternTests
{
    private static readonly MorningStarPattern P = new();
    private static PatternParameterValues V() => K.NoTrend(P);

    // 長黑 100→96 (4%)，星線 95.5→95.2 (實體 0.3 = 第一天的 0.075 倍)，紅 K 95.6→99 (收復 75%)
    private static List<StockDataPoint> Basic() => new() { K.Bar(100, 96), K.Bar(95.5, 95.2), K.Bar(95.6, 99) };

    [Fact] public void 標準晨星成立() => Assert.True(P.IsMatch(Basic(), 2, V()));
    [Fact] public void 前兩天無法判斷() => Assert.False(P.IsMatch(Basic(), 1, V()));

    [Fact]
    public void 第一天不是長黑K不成立()
    {
        Assert.False(P.IsMatch(new List<StockDataPoint> { K.Bar(96, 100), K.Bar(95.5, 95.2), K.Bar(95.6, 99) }, 2, V()));
        Assert.False(P.IsMatch(Basic(), 2, V().Set(MorningStarPattern.Day1MinBodyPct, 5)));   // 實際 4%
    }

    [Fact]
    public void 星線實體太大不成立()
    {
        var candles = new List<StockDataPoint> { K.Bar(100, 96), K.Bar(95.8, 94.2), K.Bar(95.6, 99) };   // 星線實體 1.6 = 0.4 倍
        Assert.False(P.IsMatch(candles, 2, V()));
        Assert.True(P.IsMatch(candles, 2, V().Set(MorningStarPattern.StarMaxBodyRatio, 0.5)));
    }

    [Fact]
    public void 星線位置沒有低於第一天收盤不成立() =>
        Assert.False(P.IsMatch(new List<StockDataPoint> { K.Bar(100, 96), K.Bar(96.5, 96.2), K.Bar(95.6, 99) }, 2, V()));

    [Fact]
    public void 要求跳空時_星線實體上緣須低於第一天收盤()
    {
        var candles = new List<StockDataPoint> { K.Bar(100, 96), K.Bar(96.2, 95.9), K.Bar(95.6, 99) };   // 星線上緣 96.2 高於 96
        Assert.True(P.IsMatch(candles, 2, V()));
        Assert.False(P.IsMatch(candles, 2, V().Set(MorningStarPattern.RequireGap, true)));
    }

    [Fact]
    public void 第三天收復不足不成立()
    {
        var candles = new List<StockDataPoint> { K.Bar(100, 96), K.Bar(95.5, 95.2), K.Bar(95.6, 97.5) };   // 收復 37.5%
        Assert.False(P.IsMatch(candles, 2, V()));
        Assert.True(P.IsMatch(candles, 2, V().Set(MorningStarPattern.Day3MinRecoveryPct, 30)));
    }

    [Fact]
    public void 第三天不是紅K不成立() =>
        Assert.False(P.IsMatch(new List<StockDataPoint> { K.Bar(100, 96), K.Bar(95.5, 95.2), K.Bar(99.5, 99) }, 2, V()));

    [Fact]
    public void 要求先下跌以第一天收盤為準()
    {
        // 第一天收盤 96 相對 5 天前 (106) 跌約 9.4%
        var candles = new List<StockDataPoint> { K.Bar(106, 106), K.Bar(106, 104), K.Bar(104, 102), K.Bar(102, 101), K.Bar(101, 100) };
        candles.AddRange(Basic());
        var values = PatternParameterValues.FromDefaults(P.Parameters);
        Assert.True(P.IsMatch(candles, 7, values));
        Assert.False(P.IsMatch(candles, 7, values.Set(PatternFilters.DowntrendPct, 10)));
    }
}

public class ThreeCandlesPatternTests
{
    private static readonly ThreeWhiteSoldiersPattern Soldiers = new();
    private static readonly ThreeBlackCrowsPattern Crows = new();
    private static PatternParameterValues V(ICandlePattern p) => PatternParameterValues.FromDefaults(p.Parameters);

    // 三根紅 K：100→102、101→103.5、102.5→105 (實體約 2%、開在前一根實體內、無上影線)
    private static List<StockDataPoint> Up() => new() { K.Bar(100, 102), K.Bar(101, 103.5), K.Bar(102.5, 105) };

    // 鏡像：三根黑 K
    private static List<StockDataPoint> Down() => new() { K.Bar(105, 103), K.Bar(104, 101.5), K.Bar(102.5, 100) };

    [Fact] public void 標準紅三兵成立() => Assert.True(Soldiers.IsMatch(Up(), 2, V(Soldiers)));
    [Fact] public void 標準黑三兵成立() => Assert.True(Crows.IsMatch(Down(), 2, V(Crows)));
    [Fact] public void 紅三兵不會被判成黑三兵() => Assert.False(Crows.IsMatch(Up(), 2, V(Crows)));

    [Fact]
    public void 其中一根顏色不同不成立()
    {
        var candles = Up();
        candles[1] = K.Bar(103.5, 101);
        Assert.False(Soldiers.IsMatch(candles, 2, V(Soldiers)));
    }

    [Fact]
    public void 收盤沒有逐日墊高不成立()
    {
        var candles = new List<StockDataPoint> { K.Bar(100, 102), K.Bar(100.5, 101.8), K.Bar(101, 103) };   // 第二根收盤低於第一根
        Assert.False(Soldiers.IsMatch(candles, 2, V(Soldiers)));
    }

    [Fact]
    public void 實體太小不成立()
    {
        var candles = new List<StockDataPoint> { K.Bar(100, 100.5), K.Bar(100.2, 100.8), K.Bar(100.5, 101.2) };   // 實體約 0.5%
        Assert.False(Soldiers.IsMatch(candles, 2, V(Soldiers)));
        Assert.True(Soldiers.IsMatch(candles, 2, V(Soldiers).Set(ThreeCandlesPattern.MinBodyPct, 0.4)));
    }

    [Fact]
    public void 開盤不在前一根實體內_預設不成立_取消此條件後成立()
    {
        var candles = new List<StockDataPoint> { K.Bar(100, 102), K.Bar(102.5, 104.5), K.Bar(105, 107) };   // 跳空開高
        Assert.False(Soldiers.IsMatch(candles, 2, V(Soldiers)));
        Assert.True(Soldiers.IsMatch(candles, 2, V(Soldiers).Set(ThreeCandlesPattern.RequireOpenInBody, false)));
    }

    [Fact]
    public void 上影線太長不成立()
    {
        var candles = Up();
        candles[2] = K.Bar(102.5, 105, high: 107);   // 上影線 2 > 實體 2.5 × 0.5
        Assert.False(Soldiers.IsMatch(candles, 2, V(Soldiers)));
        Assert.True(Soldiers.IsMatch(candles, 2, V(Soldiers).Set(ThreeCandlesPattern.MaxShadowRatio, 1)));
    }

    [Fact]
    public void 黑三兵看下影線()
    {
        var candles = Down();
        candles[2] = K.Bar(102.5, 100, low: 98);     // 下影線 2 > 實體 2.5 × 0.5
        Assert.False(Crows.IsMatch(candles, 2, V(Crows)));
        candles[2] = K.Bar(102.5, 100, high: 104);   // 上影線不影響黑三兵
        Assert.True(Crows.IsMatch(candles, 2, V(Crows)));
    }

    [Fact]
    public void 黑三兵要求先上漲_量到型態前一根()
    {
        // 型態前一根收盤 106 相對 5 天前 100 漲 6%
        var candles = new List<StockDataPoint> { K.Bar(100, 100), K.Bar(100, 101), K.Bar(101, 102), K.Bar(102, 104), K.Bar(104, 105), K.Bar(105, 106) };
        candles.AddRange(new[] { K.Bar(106, 104), K.Bar(105, 102.5), K.Bar(103.5, 101) });
        var values = V(Crows).Set(PatternFilters.RequireUptrend, true);
        Assert.True(Crows.IsMatch(candles, 8, values));
        Assert.False(Crows.IsMatch(candles, 8, values.Set(PatternFilters.UptrendPct, 7)));
    }

    [Fact]
    public void 方向()
    {
        Assert.Equal(PatternDirection.Bullish, Soldiers.Direction);
        Assert.Equal(PatternDirection.Bearish, Crows.Direction);
        Assert.Equal(PatternDirection.Bullish, new BullishEngulfingPattern().Direction);
        Assert.Equal(PatternDirection.Bullish, new MorningStarPattern().Direction);
    }
}

public class WinRateTests
{
    private static PatternMatch M(double? r5, double? r10, double? r20) => new() { Return5 = r5, Return10 = r10, Return20 = r20 };

    [Fact]
    public void 看多型態_上漲算勝_資料不足不計入()
    {
        var matches = new[] { M(1, 2, 3), M(-1, 2, null), M(0, -2, null), M(null, null, null) };

        var rates = PatternScanService.ComputeWinRates(matches, PatternDirection.Bullish);

        Assert.Equal(new[] { 5, 10, 20 }, rates.Select(r => r.Days));
        Assert.Equal((1, 3), (rates[0].Wins, rates[0].Total));   // 報酬 0 算輸
        Assert.Equal(33.3, rates[0].Rate);
        Assert.Equal((2, 3), (rates[1].Wins, rates[1].Total));
        Assert.Equal((1, 1), (rates[2].Wins, rates[2].Total));
        Assert.Equal(100, rates[2].Rate);
    }

    [Fact]
    public void 看空型態_下跌算勝()
    {
        var rates = PatternScanService.ComputeWinRates(new[] { M(-1, 1, -3), M(-2, -1, 2) }, PatternDirection.Bearish);
        Assert.Equal(new[] { 2, 1, 1 }, rates.Select(r => r.Wins));
    }

    [Fact]
    public void 沒有樣本時勝率為Null()
    {
        var rates = PatternScanService.ComputeWinRates(Array.Empty<PatternMatch>(), PatternDirection.Bullish);
        Assert.All(rates, r => Assert.Null(r.Rate));
    }
}

/// <summary>
/// 所有已註冊型態共用的檢查：範例 K 線 (預覽圖) 必須符合型態本身，各種參數組合都一樣。
/// 新增型態時加到 AllPatterns 即可。
/// </summary>
public class PatternExampleTests
{
    public static readonly ICandlePattern[] AllPatterns =
    {
        new BullishEngulfingPattern(), new MorningStarPattern(), new ThreeWhiteSoldiersPattern(), new ThreeBlackCrowsPattern()
    };

    public static IEnumerable<object[]> Patterns() => AllPatterns.Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(Patterns))]
    public void 預設參數的範例符合型態(ICandlePattern pattern)
    {
        var values = PatternParameterValues.FromDefaults(pattern.Parameters);
        var example = pattern.BuildExample(values);
        Assert.True(pattern.IsMatch(example.Candles, example.SignalIndex, values));
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void 每個參數調到最小與最大值時範例仍符合型態(ICandlePattern pattern)
    {
        var failures = new List<string>();
        foreach (var p in pattern.Parameters)
        {
            var candidates = p.Kind == PatternParameterKind.Toggle
                ? new[] { 0.0, 1.0 }
                : new[] { p.Min, p.Default, Math.Min(p.Max, p.Default * 3 + p.Step), p.Max };
            foreach (double v in candidates)
            {
                // 一併開啟它依附的勾選參數，讓數值真的生效
                var values = PatternParameterValues.FromDefaults(pattern.Parameters).Set(p.Key, v);
                if (p.EnabledBy != null) values.Set(p.EnabledBy, true);
                var example = pattern.BuildExample(values);
                if (!pattern.IsMatch(example.Candles, example.SignalIndex, values)) failures.Add($"{p.Key}={v}");
            }
        }
        Assert.Empty(failures);
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void 參數定義完整(ICandlePattern pattern)
    {
        Assert.NotEmpty(pattern.Name);
        Assert.True(pattern.BarCount >= 1);
        Assert.Equal(pattern.Parameters.Count, pattern.Parameters.Select(p => p.Key).Distinct().Count());
        foreach (var p in pattern.Parameters.Where(p => p.EnabledBy != null))
            Assert.Contains(pattern.Parameters, q => q.Key == p.EnabledBy && q.Kind == PatternParameterKind.Toggle);
    }
}

public class PatternParameterValuesTests
{
    private static readonly PatternParameter Days = new() { Key = "Days", Default = 5, Min = 1, Max = 60, IsInteger = true };

    [Fact]
    public void 輸入值會限制在範圍內並取整數()
    {
        var values = PatternParameterValues.FromDefaults(new[] { Days });
        Assert.True(values.TrySetFromInput(Days, "999"));
        Assert.Equal(60, values.Get("Days"));
        Assert.True(values.TrySetFromInput(Days, "2.6"));
        Assert.Equal(3, values.Get("Days"));
    }

    [Fact]
    public void 無法解析的輸入不更新()
    {
        var values = PatternParameterValues.FromDefaults(new[] { Days });
        Assert.False(values.TrySetFromInput(Days, ""));
        Assert.False(values.TrySetFromInput(Days, "abc"));
        Assert.Equal(5, values.Get("Days"));
    }

    [Fact]
    public void 畫面狀態分別保存各型態參數()
    {
        var state = new PatternScanState();
        var engulfing = new BullishEngulfingPattern();
        state.ValuesFor(engulfing).Set(BullishEngulfingPattern.MinBodyRatio, 3);
        state.ValuesFor(new MorningStarPattern());

        Assert.Equal(3, state.ValuesFor(engulfing).Get(BullishEngulfingPattern.MinBodyRatio));
        state.ResetValues(engulfing);
        Assert.Equal(1, state.ValuesFor(engulfing).Get(BullishEngulfingPattern.MinBodyRatio));
    }
}

public class PatternScanServiceTests
{
    // 產生 n 根平盤 K 棒，再於 signals 指定的索引放入看漲吞沒 (前一天黑 K + 當天紅 K)
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
            var p = list[s - 1]; p.Open = p.Close + 2; p.High = p.Open;
            var t = list[s]; t.Open = p.Close - 1; t.Close = p.Open + 1; t.High = t.Close; t.Low = t.Open;
        }
        return list;
    }

    private static readonly BullishEngulfingPattern Engulfing = new();
    private static List<PatternMatch> Find(List<StockDataPoint> candles) =>
        PatternScanService.FindMatches("2330", candles, Engulfing, K.NoTrend(Engulfing), 10, 20);

    [Fact]
    public void 小卡包含前10天與後20天()
    {
        var candles = Series(60, 30);
        var m = Assert.Single(Find(candles));
        Assert.Equal(31, m.Candles.Count);
        Assert.Equal(10, m.SignalIndex);
        Assert.Equal(2, m.PatternBarCount);
        Assert.Equal(candles[30].Date, m.SignalDate);
        Assert.Equal(candles[20].Date, m.Candles[0].Date);
        Assert.Equal(candles[50].Date, m.Candles[^1].Date);
    }

    [Fact]
    public void 靠近資料開頭與結尾時小卡較短_之後報酬為Null()
    {
        var candles = Series(40, 3, 35);
        var matches = Find(candles);

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
        var m = Assert.Single(Find(candles));
        double baseClose = candles[30].Close;
        Assert.Equal(Math.Round((candles[35].Close - baseClose) / baseClose * 100, 2), m.Return5);
        Assert.Equal(Math.Round((candles[50].Close - baseClose) / baseClose * 100, 2), m.Return20);
    }

    [Fact]
    public void 均線以完整歷史計算()
    {
        var candles = Series(60, 30);
        var m = Assert.Single(Find(candles));
        // 小卡第一根是第 20 根：MA5 = 第 16~20 根收盤平均，MA20 = 第 1~20 根收盤平均
        Assert.Equal(Math.Round(candles.Skip(16).Take(5).Average(c => c.Close), 2), m.Candles[0].Ma5);
        Assert.Equal(Math.Round(candles.Skip(1).Take(20).Average(c => c.Close), 2), m.Candles[0].Ma20);
    }

    [Fact]
    public void 均線天數不足時為Null()
    {
        var m = Assert.Single(Find(Series(40, 3)));
        Assert.Null(m.Candles[0].Ma5);
        Assert.Null(m.Candles[0].Ma20);
        Assert.NotNull(m.Candles[4].Ma5);
    }

    [Fact]
    public void 晨星的型態根數為3()
    {
        var star = new MorningStarPattern();
        var values = K.NoTrend(star);
        var example = star.BuildExample(values);

        var m = Assert.Single(PatternScanService.FindMatches("2330", example.Candles, star, values, 10, 20));
        Assert.Equal(3, m.PatternBarCount);
        Assert.Equal(example.Candles[example.SignalIndex].Date, m.SignalDate);
    }
}
