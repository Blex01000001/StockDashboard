using StockDashboard.Models.Patterns;
using StockDashboard.Services.Patterns;

namespace StockDashboard.Services
{
    /// <summary>
    /// 型態掃描頁的畫面狀態。註冊為 Scoped：Blazor Server 中每個瀏覽器分頁 (連線) 一份，
    /// 切換到其他頁面再回來時元件會重建，但狀態留在這裡，所以輸入、參數與結果都會保留。
    /// 重新整理瀏覽器 (F5) 會建立新連線，狀態會重置。
    /// </summary>
    public class PatternScanState
    {
        public string StockIdsInput { get; set; } = "2330,0050,006208";
        public string PatternKey { get; set; } = string.Empty;
        public int PageSize { get; set; } = 10;

        // 除了 5 / 10 / 20 日之外，額外計算報酬與勝率的天數 (使用者輸入原文，例如 "60,120")
        public string ExtraHorizonsInput { get; set; } = string.Empty;

        // 附圖、均線、指標參數 (與技術圖表頁共用同一套設定類別)
        public ChartDisplaySettings Display { get; } = new();

        public int PageIndex { get; set; }
        public string Message { get; set; } = string.Empty;
        public PatternScanResult? Result { get; set; }

        // 每種型態各自保留參數，切換型態再切回來不會遺失
        private readonly Dictionary<string, PatternParameterValues> _valuesByPattern = new();

        public PatternParameterValues ValuesFor(ICandlePattern pattern)
        {
            if (!_valuesByPattern.TryGetValue(pattern.Key, out var values))
            {
                values = PatternParameterValues.FromDefaults(pattern.Parameters);
                _valuesByPattern[pattern.Key] = values;
            }
            return values;
        }

        public void ResetValues(ICandlePattern pattern) =>
            _valuesByPattern[pattern.Key] = PatternParameterValues.FromDefaults(pattern.Parameters);
    }
}
