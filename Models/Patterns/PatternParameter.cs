using System.Globalization;

namespace StockDashboard.Models.Patterns
{
    public enum PatternParameterKind
    {
        Number,   // 數值輸入框
        Toggle    // 勾選框 (值為 1 / 0)
    }

    /// <summary>
    /// 型態參數的定義。頁面依此自動產生輸入框，新增型態時不必改頁面。
    /// </summary>
    public class PatternParameter
    {
        public string Key { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public PatternParameterKind Kind { get; init; } = PatternParameterKind.Number;
        public double Default { get; init; }
        public double Step { get; init; } = 0.1;
        public double Min { get; init; } = 0;
        public double Max { get; init; } = 1000;
        public bool IsInteger { get; init; }

        // 由哪個勾選參數控制是否啟用 (例如「下跌判斷天數」只在勾選「要求先下跌」時有效)
        public string? EnabledBy { get; init; }
    }

    /// <summary>
    /// 型態參數的目前數值 (Key → 數值；勾選類參數以 1 / 0 表示)。
    /// </summary>
    public class PatternParameterValues
    {
        private readonly Dictionary<string, double> _values = new();

        public static PatternParameterValues FromDefaults(IEnumerable<PatternParameter> parameters)
        {
            var values = new PatternParameterValues();
            foreach (var p in parameters) values._values[p.Key] = p.Default;
            return values;
        }

        public double Get(string key) =>
            _values.TryGetValue(key, out var v) ? v : throw new KeyNotFoundException($"型態參數不存在：{key}");

        public bool GetBool(string key) => Get(key) != 0;

        public int GetInt(string key) => (int)Math.Round(Get(key));

        public PatternParameterValues Set(string key, double value)
        {
            _values[key] = value;
            return this;
        }

        public PatternParameterValues Set(string key, bool value) => Set(key, value ? 1 : 0);

        /// <summary>
        /// 依參數定義檢查並寫入使用者輸入；無法解析時不更新並回傳 false (輸入到一半的值不影響預覽)。
        /// </summary>
        public bool TrySetFromInput(PatternParameter parameter, string? input)
        {
            if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return false;
            value = Math.Clamp(value, parameter.Min, parameter.Max);
            if (parameter.IsInteger) value = Math.Round(value);
            _values[parameter.Key] = value;
            return true;
        }

        public PatternParameterValues Clone()
        {
            var copy = new PatternParameterValues();
            foreach (var kv in _values) copy._values[kv.Key] = kv.Value;
            return copy;
        }
    }

    /// <summary>
    /// 型態範例 (參數面板旁的預覽圖)
    /// </summary>
    public class PatternExample
    {
        public List<StockDataPoint> Candles { get; set; } = new();

        // 型態最後一根 K 棒的索引；型態由 SignalIndex - BarCount + 1 開始
        public int SignalIndex { get; set; }
    }
}
