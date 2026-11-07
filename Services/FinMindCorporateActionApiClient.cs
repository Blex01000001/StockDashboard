using System.Globalization;
using System.Text.Json;
using StockDashboard.Models.Entities;

namespace StockDashboard.Services
{
    /// <summary>
    /// FinMind TaiwanStockDividendResult (除權除息結果表)：一次請求取回單一股票整段期間的事件。
    /// 欄位對應：date → ExDate、before_price → PreClose、reference_price (沒有則用 after_price) → RefPrice。
    /// 封鎖 / 達上限沿用 FinMindApiClient 的判斷 (FinMindBlockedException)。
    /// </summary>
    public class FinMindCorporateActionApiClient : IStockCorporateActionApiClient
    {
        public const string Dataset = "TaiwanStockDividendResult";

        // 證交所股利資料自民國 92 年 (2003) 起提供
        public static readonly DateTime EarliestDate = new(2003, 1, 1);

        private readonly HttpClient _http;
        private readonly string _dataUrl;
        private readonly Func<DateTime> _today;

        // today 只給測試用 (指定「今天」)；DI 只有一個建構函式可選
        public FinMindCorporateActionApiClient(HttpClient http, IConfiguration? configuration = null, Func<DateTime>? today = null)
        {
            _http = http;
            _dataUrl = configuration?["FinMind:DataUrl"] ?? FinMindApiClient.DefaultDataUrl;
            _today = today ?? (() => DateTime.Today);
        }

        public async Task<List<StockCorporateActionEntity>> GetActionsAsync(string stockId, CancellationToken cancellationToken = default)
        {
            string url = $"{_dataUrl}?dataset={Dataset}&data_id={Uri.EscapeDataString(stockId)}&start_date={EarliestDate:yyyy-MM-dd}&end_date={_today():yyyy-MM-dd}";
            using var response = await _http.GetAsync(url, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(stockId, FinMindApiClient.ParseResponse(body, (int)response.StatusCode));
        }

        /// <summary>缺日期、前收盤或參考價的列略過；同一天多筆以最後一筆為準；由舊到新</summary>
        public static List<StockCorporateActionEntity> Parse(string stockId, IEnumerable<JsonElement> data)
        {
            var byDate = new Dictionary<string, StockCorporateActionEntity>();
            foreach (var e in data)
            {
                string? date = Str(e, "date");
                double? pre = Number(e, "before_price");
                double? reference = Number(e, "reference_price") ?? Number(e, "after_price");
                if (string.IsNullOrEmpty(date) || pre is not > 0 || reference is not > 0) continue;
                byDate[date] = new StockCorporateActionEntity { StockId = stockId, ExDate = date, PreClose = pre.Value, RefPrice = reference.Value };
            }
            return byDate.Values.OrderBy(a => a.ExDate, StringComparer.Ordinal).ToList();
        }

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static double? Number(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            return v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double p) ? p : null;
        }
    }
}
